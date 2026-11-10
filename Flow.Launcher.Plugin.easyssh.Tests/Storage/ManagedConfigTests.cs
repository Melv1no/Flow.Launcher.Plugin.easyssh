using Flow.Launcher.Plugin.EasySsh.Core;
using Flow.Launcher.Plugin.EasySsh.Storage;

namespace Flow.Launcher.Plugin.EasySsh.Tests.Storage;

public class ManagedConfigTests
{
    private static SshHostEntry Host(string alias, params string[] options) =>
        new(alias, options.Select(SshConfigText.SplitDirective).Select(pair => new SshOption(pair.Keyword, pair.Value)));

    private static string Lines(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    [Fact]
    public void A_new_file_has_a_header_then_the_hosts()
    {
        var config = ManagedConfig.CreateEmpty();

        config.Upsert(Host("prod", "HostName 10.0.0.5", "User admin"));
        config.Upsert(Host("dev", "HostName dev.local"));

        Assert.Equal(
            Lines(
                ManagedConfig.Header[0],
                ManagedConfig.Header[1],
                "",
                "Host prod",
                "    HostName 10.0.0.5",
                "    User admin",
                "",
                "Host dev",
                "    HostName dev.local"),
            config.Serialize());
    }

    [Fact]
    public void A_host_is_rewritten_in_place_and_the_rest_is_kept()
    {
        var config = ManagedConfig.Parse("""
            # my notes
            Host prod
              HostName old
              # a comment inside the block

            # about dev
            Host dev
              HostName dev.local
            Host a b
              User shared
            """);

        config.Upsert(Host("prod", "HostName new", "Port 2222"));

        Assert.Equal(
            Lines(
                "# my notes",
                "Host prod",
                "    HostName new",
                "    Port 2222",
                "  # a comment inside the block",
                "",
                "# about dev",
                "Host dev",
                "  HostName dev.local",
                "Host a b",
                "  User shared"),
            config.Serialize());
    }

    [Fact]
    public void Removing_a_host_keeps_the_comment_of_the_next_one()
    {
        var config = ManagedConfig.Parse("""
            Host prod
              HostName prod.local

            # about dev
            Host dev
              HostName dev.local
            """);

        Assert.True(config.Remove("PROD"));
        Assert.False(config.Remove("prod"));

        Assert.Equal(Lines("# about dev", "Host dev", "  HostName dev.local"), config.Serialize());
        Assert.Equal(["dev"], config.Hosts.Select(host => host.Alias));
    }

    [Fact]
    public void Hosts_are_read_with_their_options()
    {
        var config = ManagedConfig.Parse("Host prod\n  HostName=10.0.0.5\n  IdentityFile \"~/.ssh/my key\"\nMatch all\n  User x\n");

        var host = Assert.Single(config.Hosts);

        Assert.Equal("10.0.0.5", host.HostName);
        Assert.Equal("~/.ssh/my key", host.IdentityFile);
        Assert.Equal([new SshOption("HostName", "10.0.0.5"), new SshOption("IdentityFile", "\"~/.ssh/my key\"")], host.Options);
    }

    [Fact]
    public void A_host_can_be_renamed()
    {
        var config = ManagedConfig.Parse("Host prod\n  HostName p\nHost dev\n  HostName d\n");

        config.Upsert(Host("production", "HostName p"), previousAlias: "prod");

        Assert.Equal(["production", "dev"], config.Hosts.Select(host => host.Alias));
    }

    [Fact]
    public void Aliases_stay_unique()
    {
        var config = ManagedConfig.Parse("Host prod\nHost dev\nHost a b\n");

        Assert.Throws<ArgumentException>(() => config.Upsert(Host("dev"), previousAlias: "prod"));
        Assert.Throws<ArgumentException>(() => config.Upsert(Host("b")));
        Assert.Throws<ArgumentException>(() => config.Upsert(Host("not valid")));
        Assert.True(config.Declares("A"));
        Assert.Null(config.Find("a"));
    }

    [Theory]
    [InlineData("Host", "other")]
    [InlineData("Include", "x")]
    [InlineData("Port", " ")]
    [InlineData("Bad Keyword", "x")]
    [InlineData("User", "a\nHost evil")]
    public void Options_that_would_break_the_file_are_refused(string keyword, string value)
    {
        Assert.False(ManagedConfig.IsValidOption(new SshOption(keyword, value)));
        Assert.Throws<ArgumentException>(() => ManagedConfig.CreateEmpty().Upsert(new SshHostEntry("h", [new SshOption(keyword, value)])));
    }

    [Fact]
    public void A_byte_order_mark_is_dropped()
    {
        var config = ManagedConfig.Parse("﻿Host prod\n");

        Assert.Equal(Lines("Host prod"), config.Serialize());
    }
}
