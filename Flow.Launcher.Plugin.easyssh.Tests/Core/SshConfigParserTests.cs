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
        Assert.Equal(new SshConfigHost("web", "10.0.0.5", "admin", "2222", config), hosts[0]);
        Assert.Equal("admin@10.0.0.5:2222", hosts[0].Target);
        Assert.Equal("db.internal", hosts[2].HostName);
        Assert.Equal("ssh web", hosts[0].Command);
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
}
