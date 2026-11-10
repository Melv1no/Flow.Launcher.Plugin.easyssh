using Flow.Launcher.Plugin.EasySsh.Core;

namespace Flow.Launcher.Plugin.EasySsh.Tests.Core;

public class SshConfigParserTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Reads_aliases_and_their_details()
    {
        var config = _temp.Write("config", """
            # personal servers
            Host web
                HostName 10.0.0.5
                User admin
                Port 2222
                User ignored-second-value

            Host db db-replica
              HostName db.internal
            """);

        var hosts = SshConfigParser.Load(config).Hosts;

        Assert.Equal(["web", "db", "db-replica"], hosts.Select(host => host.Alias));
        Assert.Equal(new SshConfigHost("web", "10.0.0.5", "admin", "2222", null, null, config), hosts[0]);
        Assert.Equal("admin@10.0.0.5:2222", hosts[0].Target);
        Assert.Equal("db.internal", hosts[2].HostName);
        Assert.Equal("ssh web", hosts[0].Command);
    }

    [Fact]
    public void Reads_jump_hosts_and_identity_files()
    {
        var config = _temp.Write("config", """
            Host internal
                ProxyJump bastion
                IdentityFile "~/.ssh/my key"
                IdentityFile ~/.ssh/second
            """);

        var host = Assert.Single(SshConfigParser.Load(config).Hosts);

        Assert.Equal("bastion", host.ProxyJump);
        Assert.Equal("~/.ssh/my key", host.IdentityFile);
    }

    [Fact]
    public void Duplicated_aliases_are_reported_with_the_ignored_file()
    {
        var other = _temp.Write("other", "Host web\nHost unique");
        var config = _temp.Write("config", $"Host web\nInclude \"{other}\"");

        var snapshot = SshConfigParser.Load(config);

        Assert.Equal(["web", "unique"], snapshot.Hosts.Select(host => host.Alias));
        Assert.Equal([other], snapshot.Duplicates["WEB"]);
        Assert.Equal([config, other], snapshot.Files);
        Assert.Same(snapshot.Hosts[0], snapshot.Find("Web"));
    }

    [Fact]
    public void Include_patterns_are_resolved_like_ssh_does()
    {
        _temp.Write(Path.Combine("d", "a.conf"), "Host a");
        _temp.Write(Path.Combine("d", "b.conf"), "Host b");

        Assert.Equal(
            [_temp.Combine("d", "a.conf"), _temp.Combine("d", "b.conf")],
            SshConfigParser.ResolveInclude("d/*.conf", _temp.Path));
        Assert.Equal([_temp.Combine("missing")], SshConfigParser.ResolveInclude("missing", _temp.Path));
    }

    [Fact]
    public void Ignores_patterns_negations_and_match_blocks()
    {
        var config = _temp.Write("config", """
            Host *
                ServerAliveInterval 60
            Host *.example.com bast?on !forbidden real
                User root
            Match host real exec "true"
                User nobody
            """);

        var host = Assert.Single(SshConfigParser.Load(config).Hosts);

        Assert.Equal("real", host.Alias);
        Assert.Equal("root", host.User);
    }

    [Fact]
    public void Supports_equal_signs_case_and_quotes()
    {
        var config = _temp.Write("config", """
            HOST=alpha
              hostname = 192.168.1.10
            Host "with space"
              Port=22
            """);

        var hosts = SshConfigParser.Load(config).Hosts;

        Assert.Equal("192.168.1.10", hosts[0].HostName);
        Assert.Equal("with space", hosts[1].Alias);
        Assert.Equal("ssh \"with space\"", hosts[1].Command);
    }

    [Fact]
    public void First_declaration_of_an_alias_wins()
    {
        var config = _temp.Write("config", """
            Host web
              HostName first
            Host WEB
              HostName second
            """);

        var host = Assert.Single(SshConfigParser.Load(config).Hosts);

        Assert.Equal("first", host.HostName);
    }

    [Fact]
    public void Follows_includes_in_order_with_globs_and_variables()
    {
        _temp.Write(Path.Combine("config.d", "20-b.conf"), "Host b");
        _temp.Write(Path.Combine("config.d", "10-a.conf"), "Host a\nInclude nested");
        _temp.Write("nested", "Host nested");
        _temp.Write(Path.Combine("extra", "more"), "Host extra");
        Environment.SetEnvironmentVariable("EASYSSH_TEST_EXTRA", _temp.Combine("extra"));
        var config = _temp.Write("config", """
            Host first
            Include config.d/*.conf
            Include ${EASYSSH_TEST_EXTRA}/more
            Host last
            """);

        var hosts = SshConfigParser.Load(config, _temp.Path).Hosts;

        Assert.Equal(["first", "a", "nested", "b", "extra", "last"], hosts.Select(host => host.Alias));
    }

    [Fact]
    public void Survives_include_cycles_and_missing_files()
    {
        var config = _temp.Write("config", """
            Include config
            Include does-not-exist
            Host only
            """);

        Assert.Equal("only", Assert.Single(SshConfigParser.Load(config, _temp.Path).Hosts).Alias);
    }

    [Fact]
    public void Missing_config_has_no_hosts()
    {
        Assert.Empty(SshConfigParser.Load(_temp.Combine("nothing-here")).Hosts);
    }

    [Fact]
    public void Target_is_null_without_details()
    {
        var config = _temp.Write("config", "Host bare");

        Assert.Null(Assert.Single(SshConfigParser.Load(config).Hosts).Target);
    }

    [Fact]
    public void Snapshot_becomes_stale_when_a_file_changes()
    {
        var config = _temp.Write("config", "Host one");
        var snapshot = SshConfigParser.Load(config);
        Assert.False(snapshot.IsStale());

        File.WriteAllText(config, "Host one\nHost two");
        File.SetLastWriteTimeUtc(config, DateTime.UtcNow.AddMinutes(1));

        Assert.True(snapshot.IsStale());
    }

    [Fact]
    public void Snapshot_becomes_stale_when_an_included_folder_changes()
    {
        var folder = _temp.Combine("config.d");
        _temp.Write(Path.Combine("config.d", "a.conf"), "Host a");
        var config = _temp.Write("config", "Include config.d/*");
        var snapshot = SshConfigParser.Load(config, _temp.Path);

        _temp.Write(Path.Combine("config.d", "b.conf"), "Host b");
        Directory.SetLastWriteTimeUtc(folder, DateTime.UtcNow.AddMinutes(1));

        Assert.True(snapshot.IsStale());
    }

    [Fact]
    public void Snapshot_becomes_stale_when_the_config_is_created()
    {
        var config = _temp.Combine("config");
        var snapshot = SshConfigParser.Load(config);

        File.WriteAllText(config, "Host new");

        Assert.True(snapshot.IsStale());
    }

    [Theory]
    [InlineData("*.corp", "web.corp", true)]
    [InlineData("*.corp", "web", false)]
    [InlineData("WEB?", "web1", true)]
    [InlineData("* !bastion", "bastion", false)]
    [InlineData("* !bastion", "web", true)]
    [InlineData("!bastion", "web", false)]
    public void Host_lines_match_like_ssh_does(string patterns, string host, bool expected)
    {
        Assert.Equal(expected, SshConfigParser.MatchesHostLine(patterns.Split(' '), host));
    }

    [Fact]
    public void Host_patterns_must_apply_the_same_way_to_an_alias_and_its_destination()
    {
        var config = _temp.Write("config", "Host *.corp\n  ProxyJump bastion\nHost *\n  ServerAliveInterval 60\n");

        var snapshot = SshConfigParser.Load(config);

        Assert.False(snapshot.HaveSameRules("web", "web.corp"));
        Assert.True(snapshot.HaveSameRules("web", "10.0.0.5"));
        Assert.True(snapshot.HaveSameRules("WEB.corp", "web.corp"));
    }

    [Fact]
    public void Match_originalhost_makes_the_rules_unknown()
    {
        var config = _temp.Write("config", "Match originalhost web\n  User x\n");

        Assert.False(SshConfigParser.Load(config).HaveSameRules("web", "10.0.0.5"));
        Assert.True(SshConfigParser.Load(_temp.Write("other", "Match all\n  User x\n")).HaveSameRules("web", "10.0.0.5"));
    }
}
