using System.Windows;
using Flow.Launcher.Plugin.EasySsh.Core;
using Flow.Launcher.Plugin.EasySsh.Storage;
using NSubstitute;

namespace Flow.Launcher.Plugin.EasySsh.Tests;

public class PluginTests
{
    private static PluginFixture WithConfig(string config)
    {
        var fixture = new PluginFixture();
        fixture.WriteConfig(config);
        return fixture;
    }

    private static PluginFixture WithCommands(params (string Name, string Command)[] commands)
    {
        var fixture = new PluginFixture();
        foreach (var (name, command) in commands)
        {
            fixture.Services.Store.Update(data => data.UpsertCommand(new SavedCommand(name, command)));
        }

        return fixture;
    }

    // ------------------------------------------------------------------ startup

    [Fact]
    public void First_start_without_old_data_shows_the_empty_state_and_writes_nothing_in_ssh()
    {
        using var fixture = new PluginFixture();

        var results = fixture.Query("");

        Assert.Equal("No host yet", results[0].Title);
        Assert.Contains(results, result => result.Title == "Add a host");
        Assert.NotNull(fixture.Data.LegacyImport);
        Assert.False(Directory.Exists(Path.GetDirectoryName(fixture.SshConfigPath)));
        fixture.Api.DidNotReceive().ShowMsg(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public void First_start_turns_the_profiles_of_version_1_into_hosts_once()
    {
        using var fixture = new PluginFixture(temp => temp.Write(Path.Combine("ssh", "profiles.json"), """
            { "1": { "Id": 1, "Name": "prod", "Command": "root@10.0.0.1 -p 2222" } }
            """));

        var host = Assert.Single(fixture.ManagedHosts);
        Assert.Equal("prod", host.Alias);
        Assert.Equal(["HostName 10.0.0.1", "User root", "Port 2222"], host.Options.Select(option => option.ToString()));
        Assert.Contains("Include easyssh.config", File.ReadAllText(fixture.SshConfigPath));
        Assert.Equal(1, fixture.Data.LegacyImport!.Hosts);
        fixture.Api.Received(1).ShowMsg(
            "EasySSH",
            Arg.Is<string>(text => text.StartsWith("Imported from ~/.ssh/profiles.json: 1 host(s) into ~/.ssh/easyssh.config") && text.Contains("Include easyssh.config")),
            Arg.Any<string>());
        Assert.Equal("root@10.0.0.1:2222 · ~/.ssh/easyssh.config", fixture.Query("prod")[0].SubTitle);

        // Next start: nothing imported again, and the old file is still there.
        var restarted = new Main();
        restarted.Initialize(fixture.Api, fixture.DataDirectory, fixture.LegacyPath, fixture.SshConfigPath);
        Assert.Single(fixture.ManagedHosts);
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
            plugin.Initialize(api, dataDirectory, temp.Combine("profiles.json"), temp.Combine("ssh", "config"));
            var result = Assert.Single(plugin.Answer("anything", "ssh"));
            Assert.Equal("EasySSH could not start", result.Title);
        }

        await plugin.ReloadDataAsync();

        Assert.NotNull(plugin.Services);
        plugin.Services.OpenSshInstalled = true;
        Assert.Equal("No host yet", plugin.Answer("", "ssh").OrderByDescending(result => result.Score).First().Title);
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
    public void Hosts_of_the_ssh_config_are_ranked_prefixes_first_then_alphabetically()
    {
        using var fixture = WithConfig("Host preprod-web\n  HostName 10.0.0.3\nHost web-02\nHost web-01\n");

        var titles = fixture.Query("web").Select(result => result.Title).ToList();

        Assert.Equal(["web-01", "web-02", "preprod-web"], titles.Take(3));
        Assert.Equal("Connect to web", titles[^1]);
    }

    [Fact]
    public void Search_tolerates_typos()
    {
        using var fixture = WithConfig("Host preprod-web\n");

        Assert.Equal("preprod-web", fixture.Query("prepord")[0].Title);
    }

    [Fact]
    public void Host_results_are_complete()
    {
        using var fixture = WithConfig("Host prod\n  HostName 10.0.0.5\n  User admin\n  Port 2222\n  ProxyJump bastion\n");

        var result = fixture.Query("pro")[0];

        Assert.Equal("prod", result.Title);
        Assert.Equal("admin@10.0.0.5:2222 · via bastion · ~/.ssh/config", result.SubTitle);
        Assert.Equal("ssh prod", result.CopyText);
        Assert.Equal("ssh prod", result.AutoCompleteText);
        Assert.Equal("host:prod", result.RecordKey);
        Assert.Equal([0, 1, 2], result.TitleHighlightData);
        Assert.True(result.AddSelectedCount);
        Assert.Equal("prod", fixture.Query("bastion")[0].Title);
    }

    [Fact]
    public void Connecting_runs_ssh_with_the_alias_in_the_selected_terminal()
    {
        using var fixture = WithConfig("Host prod\n  HostName 10.0.0.5\n");

        fixture.Query("prod")[0].Run();

        var started = Assert.Single(fixture.Started);
        Assert.EndsWith("cmd.exe", started.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/s /c \"title prod & ssh prod & if errorlevel 255 pause\"", started.Arguments);
    }

    [Fact]
    public void Graphical_programs_are_started_without_a_terminal()
    {
        // Issue #2: KiTTY or PuTTY in a terminal left an empty console open behind them.
        using var fixture = WithCommands(("files", "explorer.exe /n"));

        fixture.Query("files")[0].Run();

        var started = Assert.Single(fixture.Started);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), started.FileName, ignoreCase: true);
        Assert.Equal("/n", started.Arguments);
    }

    [Fact]
    public void Duplicated_aliases_are_flagged()
    {
        using var fixture = new PluginFixture();
        fixture.WriteSshFile("other", "Host web\n  HostName ignored\n");
        fixture.WriteConfig("Host web\n  HostName used\nInclude other\n");

        var result = fixture.Query("web")[0];

        Assert.Equal("used · ~/.ssh/config · ⚠ also declared in ~/.ssh/other, ignored by ssh", result.SubTitle);
    }

    [Fact]
    public void Hidden_hosts_are_not_listed_and_can_be_shown_again()
    {
        using var fixture = WithConfig("Host github.com\nHost prod\n");
        fixture.Services.Store.Update(data => data.SetHidden("github.com", hidden: true));

        Assert.DoesNotContain(fixture.Query(""), result => result.Title == "github.com");

        var hidden = fixture.Query("hidden")[0];
        Assert.Equal("github.com", hidden.Title);
        hidden.Run();

        Assert.False(fixture.Data.IsHidden("github.com"));
        Assert.Contains(fixture.Query(""), result => result.Title == "github.com");
    }

    [Fact]
    public void Servers_of_known_hosts_are_suggested_below_the_hosts()
    {
        using var fixture = WithConfig("Host prod\n  HostName 10.0.0.5\nHost 10.9-lab\n");
        File.WriteAllText(fixture.KnownHostsPath, "10.0.0.5 ssh-ed25519 AAA\n10.9.8.7 ssh-ed25519 BBB\n[10.9.8.8]:2222 ssh-rsa CCC\n");

        var results = fixture.Query("10.9");

        Assert.Equal(["10.9-lab", "10.9.8.7", "10.9.8.8:2222"], results.Take(3).Select(result => result.Title));
        Assert.Equal("Already connected (known_hosts) · ssh -p 2222 10.9.8.8", results[2].SubTitle);
        Assert.Equal("Connect to 10.9", results[^1].Title);
        Assert.DoesNotContain(fixture.Query("10.0.0.5"), result => result.Title == "10.0.0.5");

        fixture.Services.Store.Update(data => data with { SuggestKnownHosts = false });
        Assert.DoesNotContain(fixture.Query("10.9"), result => result.Title == "10.9.8.7");
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
        using var fixture = WithConfig("Host prod\n");

        Assert.Equal("prod", fixture.Query("prod", actionKeyword: "")[0].AutoCompleteText);
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

        result.Run();
        Assert.Contains("& ssh root@127.0.0.1 &", Assert.Single(fixture.Started).Arguments);
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
        using var fixture = WithConfig("Host dev\n");

        var results = fixture.Query("d");

        Assert.Equal("Connect without saving", results[0].Title);
        Assert.Contains(results, result => result.Title == "dev");
    }

    // ------------------------------------------------------------------ add

    [Fact]
    public void Add_writes_a_host_to_easyssh_config()
    {
        using var fixture = new PluginFixture();

        var result = Assert.Single(fixture.Query("add prod root@10.0.0.1 -p 2222"));
        Assert.Equal("Add host \"prod\"", result.Title);
        Assert.Equal("Connects to root@10.0.0.1:2222 · written to ~/.ssh/easyssh.config", result.SubTitle);
        Assert.Equal("Host prod\n    HostName 10.0.0.1\n    User root\n    Port 2222", result.SubTitleToolTip);

        Assert.False(result.Run());

        Assert.Equal(["HostName 10.0.0.1", "User root", "Port 2222"], Assert.Single(fixture.ManagedHosts).Options.Select(option => option.ToString()));
        Assert.Contains("Include easyssh.config", File.ReadAllText(fixture.SshConfigPath));
        fixture.Api.Received().ChangeQuery("ssh prod", true);
        fixture.Api.Received(1).ShowMsg("EasySSH", Arg.Is<string>(text => text.Contains("Include easyssh.config")), Arg.Any<string>());
        Assert.Equal("prod", fixture.Query("prod")[0].Title);
    }

    [Fact]
    public void Add_turns_a_name_with_spaces_into_an_alias()
    {
        using var fixture = new PluginFixture();

        var result = fixture.Query("add \"My Server\" ssh -i \"C:\\my keys\\id\" admin@host")[0];
        Assert.Equal("Add host \"My-Server\"", result.Title);
        result.Run();

        var host = Assert.Single(fixture.ManagedHosts);
        Assert.Equal("My-Server", host.Alias);
        Assert.Contains(new SshOption("IdentityFile", "\"C:/my keys/id\""), host.Options);
    }

    [Fact]
    public void Add_updates_a_host_of_easyssh_config()
    {
        using var fixture = new PluginFixture();
        fixture.Query("add prod old@prod")[0].Run();

        var result = fixture.Query("add PROD new@prod")[0];

        Assert.Equal("Update host \"PROD\"", result.Title);
        result.Run();
        var host = Assert.Single(fixture.ManagedHosts);
        Assert.Equal("PROD", host.Alias);
        Assert.Equal("new", host.User);
    }

    [Fact]
    public void Add_never_shadows_a_host_of_your_own_files()
    {
        using var fixture = WithConfig("Host prod\n  HostName 10.0.0.9\n");

        var result = Assert.Single(fixture.Query("add prod root@10.0.0.1"));

        Assert.Equal("\"prod\" is already a host of your SSH configuration", result.Title);
        Assert.Equal(Icons.Warning, result.IcoPath);
        result.Run();
        Assert.Equal(fixture.SshConfigPath, Assert.Single(fixture.Started).FileName);
        Assert.False(File.Exists(fixture.ManagedPath));
    }

    [Fact]
    public void What_ssh_config_cannot_express_is_saved_as_a_command()
    {
        using var fixture = new PluginFixture();

        var result = fixture.Query("add box wsl ssh box")[0];
        Assert.Equal("Add command \"box\"", result.Title);
        Assert.Equal("Command: wsl ssh box · saved in EasySSH, because it runs wsl, not ssh", result.SubTitle);
        result.Run();

        Assert.Equal([new SavedCommand("box", "wsl ssh box")], fixture.Data.Commands);
        Assert.False(File.Exists(fixture.ManagedPath));
        Assert.EndsWith("ssh_config has no equivalent for -N", fixture.Query("add tunnel ssh -N -L 8080:localhost:80 box")[0].SubTitle);
    }

    [Fact]
    public void Options_on_top_of_an_existing_host_make_a_command()
    {
        using var fixture = WithConfig("Host bastion\n  HostName 10.0.0.1\n");

        Assert.EndsWith(
            "the host bastion already exists and ssh_config can't add options to it",
            fixture.Query("add b2 bastion -p 2222")[0].SubTitle);
    }

    [Fact]
    public void Saving_a_command_as_a_host_replaces_it()
    {
        using var fixture = WithCommands(("box", "plink box"));

        var result = fixture.Query("add box admin@box")[0];

        Assert.Equal("Update host \"box\"", result.Title);
        result.Run();
        Assert.Empty(fixture.Data.Commands);
        Assert.Equal("box", Assert.Single(fixture.ManagedHosts).Alias);
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
    public void Add_alone_shows_the_usage_and_matching_hosts()
    {
        using var fixture = WithConfig("Host addons\n");

        var results = fixture.Query("add");

        Assert.Equal("Add a host", results[0].Title);
        Assert.Contains(results, result => result.Title == "addons");
    }

    // ------------------------------------------------------------------ edit / remove

    [Fact]
    public void Edit_puts_a_host_of_easyssh_config_back_in_the_search_bar()
    {
        using var fixture = new PluginFixture();
        fixture.Query("add prod root@10.0.0.1 -p 2222 -L 8080:localhost:80")[0].Run();

        var result = fixture.Query("edit pro")[0];

        Assert.Equal("ssh edit prod", result.AutoCompleteText);
        Assert.False(result.Run());
        fixture.Api.Received().ChangeQuery("ssh add prod ssh root@10.0.0.1 -p 2222 -L 8080:localhost:80", false);
    }

    [Fact]
    public void Edit_opens_the_file_of_the_other_hosts()
    {
        using var fixture = WithConfig("Host web\n");

        var result = fixture.Query("edit web")[0];

        Assert.Equal("Declared in ~/.ssh/config: press Enter to open the file", result.SubTitle);
        result.Run();
        Assert.Equal(fixture.SshConfigPath, Assert.Single(fixture.Started).FileName);
    }

    [Fact]
    public void Edit_puts_a_command_back_in_the_search_bar()
    {
        using var fixture = WithCommands(("My box", "wsl ssh box"));

        fixture.Query("edit my")[0].Run();

        fixture.Api.Received().ChangeQuery("ssh add \"My box\" wsl ssh box", false);
    }

    [Fact]
    public void Remove_asks_for_confirmation()
    {
        using var fixture = new PluginFixture();
        fixture.Query("add prod root@prod")[0].Run();
        fixture.Query("add dev root@dev")[0].Run();
        fixture.Api.ShowMsgBox(default!, default!, default, default, default).ReturnsForAnyArgs(MessageBoxResult.No, MessageBoxResult.Yes);

        var result = fixture.Query("remove prod")[0];
        result.Run();
        Assert.Equal(2, fixture.ManagedHosts.Count);

        result.Run();
        Assert.Equal(["dev"], fixture.ManagedHosts.Select(host => host.Alias));
        fixture.Api.Received().ReQuery();
    }

    [Fact]
    public void Hosts_of_your_own_files_are_never_removed()
    {
        using var fixture = WithConfig("Host web\n");

        var result = fixture.Query("remove web")[0];

        Assert.Equal(Icons.App, result.IcoPath);
        Assert.Equal("Declared in ~/.ssh/config: press Enter to open the file", result.SubTitle);
        result.Run();
        Assert.Equal("Host web\n", File.ReadAllText(fixture.SshConfigPath));
    }

    [Fact]
    public void Commands_can_be_removed()
    {
        using var fixture = WithCommands(("box", "wsl ssh box"));
        fixture.Api.ShowMsgBox(default!, default!, default, default, default).ReturnsForAnyArgs(MessageBoxResult.Yes);

        fixture.Query("rm box")[0].Run();

        Assert.Empty(fixture.Data.Commands);
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

    // ------------------------------------------------------------------ help, files & context menus

    [Fact]
    public void Help_lists_every_command()
    {
        using var fixture = new PluginFixture();

        var completions = fixture.Query("help").Select(result => result.AutoCompleteText).ToList();

        Assert.Equal(
            ["ssh add ", "ssh edit ", "ssh remove ", "ssh hosts ", "ssh d ", "ssh terminal ", "ssh config ", "ssh hidden "],
            completions.Take(8));
    }

    [Fact]
    public void Config_lists_the_ssh_files()
    {
        using var fixture = new PluginFixture();
        fixture.WriteSshFile("other", "Host b\nHost c\n");
        fixture.WriteConfig("Host a\nInclude other\n");

        var results = fixture.Query("config");

        Assert.Equal(["~/.ssh/config", "~/.ssh/other", "~/.ssh/easyssh.config"], results.Select(result => result.Title));
        Assert.Equal("Hosts: 2 · press Enter to open", results[1].SubTitle);
        Assert.Equal("Created when you add a host with EasySSH", results[2].SubTitle);
        Assert.Null(results[2].Action);
    }

    [Fact]
    public void A_missing_include_is_reported_and_repaired()
    {
        using var fixture = new PluginFixture();
        fixture.Query("add prod root@prod")[0].Run();
        fixture.WriteConfig("Host web\n");

        var warning = fixture.Query("")[0];
        Assert.Equal("ssh doesn't see the hosts saved with EasySSH", warning.Title);
        warning.Run();

        Assert.StartsWith("# Added by EasySSH", File.ReadAllText(fixture.SshConfigPath));
        Assert.DoesNotContain(fixture.Query(""), result => result.Title == warning.Title);
        Assert.Contains(fixture.Query(""), result => result.Title == "prod");
    }

    [Fact]
    public void Host_context_menu_of_a_saved_host()
    {
        using var fixture = new PluginFixture();
        fixture.Query("add prod root@prod")[0].Run();

        var titles = fixture.Plugin.LoadContextMenus(fixture.Query("prod")[0]).Select(result => result.Title).ToList();

        Assert.Equal(["Copy the command", "Open an SFTP session", "Edit", "Remove", "Hide from EasySSH", "Open the configuration file"], titles.Take(6));
        Assert.Contains("Open in Windows PowerShell", titles);
        Assert.DoesNotContain("Open in Command Prompt", titles);
    }

    [Fact]
    public void Hosts_of_your_own_files_cannot_be_removed_from_the_context_menu()
    {
        using var fixture = WithConfig("Host web\n");

        var titles = fixture.Plugin.LoadContextMenus(fixture.Query("web")[0]).Select(result => result.Title).ToList();

        Assert.DoesNotContain("Remove", titles);
        Assert.Contains("Hide from EasySSH", titles);
    }

    [Fact]
    public void Context_menu_actions()
    {
        using var fixture = WithConfig("Host prod\n");
        var menu = fixture.Plugin.LoadContextMenus(fixture.Query("prod")[0]);

        menu.Single(result => result.Title == "Copy the command").Run();
        fixture.Api.Received().CopyToClipboard("ssh prod", false, true);

        menu.Single(result => result.Title == "Open an SFTP session").Run();
        Assert.Contains("& sftp prod &", Assert.Single(fixture.Started).Arguments);

        menu.Single(result => result.Title == "Hide from EasySSH").Run();
        Assert.True(fixture.Data.IsHidden("prod"));
    }

    [Fact]
    public void Direct_connection_can_be_saved_as_a_host()
    {
        using var fixture = new PluginFixture();
        var direct = fixture.Query("d admin@192.0.2.4")[0];

        fixture.Plugin.LoadContextMenus(direct).Single(result => result.Title == "Save as a host").Run();

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

    // ------------------------------------------------------------------ review findings

    [Fact]
    public void Editing_a_host_whose_name_ends_with_dot_com_keeps_it_a_host()
    {
        using var fixture = new PluginFixture();
        fixture.Query("add web ssh -p 2222 web.example.com")[0].Run();

        fixture.Query("edit web")[0].Run();
        fixture.Api.Received().ChangeQuery("ssh add web ssh web.example.com -p 2222", false);

        var update = fixture.Query("add web ssh web.example.com -p 2222")[0];
        Assert.Equal("Update host \"web\"", update.Title);
        update.Run();
        Assert.Equal(["HostName web.example.com", "Port 2222"], Assert.Single(fixture.ManagedHosts).Options.Select(option => option.ToString()));
    }

    [Fact]
    public void A_host_saved_with_easyssh_is_never_replaced_by_a_command()
    {
        using var fixture = new PluginFixture();
        fixture.Query("add prod root@10.0.0.1")[0].Run();

        var result = Assert.Single(fixture.Query("add prod wsl ssh box"));

        Assert.Equal("\"prod\" is a host saved with EasySSH", result.Title);
        Assert.StartsWith("This command can't replace it, because it runs wsl, not ssh.", result.SubTitle);
        Assert.Null(result.Action);
        Assert.Single(fixture.ManagedHosts);
    }

    [Fact]
    public void A_destination_using_host_patterns_stays_a_command()
    {
        using var fixture = WithConfig("Host *.corp\n  ProxyJump bastion\n");

        var result = fixture.Query("add web web.corp")[0];

        Assert.Equal("Add command \"web\"", result.Title);
        Assert.EndsWith("the Host patterns of your configuration that apply to web.corp would not apply to web", result.SubTitle);
    }

    [Fact]
    public void A_host_refused_by_ssh_is_not_written()
    {
        using var fixture = new PluginFixture();

        fixture.Query("add bad admin@10.0.0.1 -o NoSuchKeyword=x")[0].Run();

        fixture.Api.Received().ShowMsgError(
            "EasySSH could not save your changes",
            Arg.Is<string>(text => text.StartsWith("ssh refuses this configuration, nothing was saved:") && text.Contains("nosuchkeyword", StringComparison.OrdinalIgnoreCase)));
        Assert.False(File.Exists(fixture.ManagedPath));
        Assert.False(File.Exists(fixture.SshConfigPath));
    }

    [Fact]
    public void A_host_named_after_its_server_says_it_applies_everywhere()
    {
        using var fixture = new PluginFixture();

        var result = fixture.Query("add github.com git@github.com -i ~/.ssh/work")[0];

        Assert.Equal("Add host \"github.com\"", result.Title);
        Assert.EndsWith("applies to every connection to github.com, also outside EasySSH", result.SubTitle);
    }

    [Fact]
    public void A_command_named_like_one_of_your_hosts_can_still_be_changed()
    {
        using var fixture = WithConfig("Host nas\n  HostName 10.0.0.2\n");
        fixture.Services.Store.Update(data => data.UpsertCommand(new SavedCommand("nas", "ssh admin@nas")));

        var result = fixture.Query("add nas ssh admin@nas -p 2200")[0];

        Assert.Equal("Update command \"nas\"", result.Title);
        Assert.EndsWith("the host nas already exists in your configuration", result.SubTitle);
        result.Run();
        Assert.Equal("ssh admin@nas -p 2200", fixture.Data.FindCommand("nas")!.Command);
    }
}
