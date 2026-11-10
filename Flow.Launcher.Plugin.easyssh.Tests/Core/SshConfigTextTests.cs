using Flow.Launcher.Plugin.EasySsh.Core;

namespace Flow.Launcher.Plugin.EasySsh.Tests.Core;

public class SshConfigTextTests
{
    [Theory]
    [InlineData("prod", "prod")]
    [InlineData("web-01", "web-01")]
    [InlineData("Préprod web", "Preprod-web")]
    [InlineData("  my   server  ", "my-server")]
    [InlineData("admin@host", "admin-host")]
    [InlineData("prod.example.com", "prod.example.com")]
    [InlineData("C:\\weird/name", "C-weird-name")]
    [InlineData("!!!", null)]
    [InlineData("", null)]
    public void Names_become_aliases(string name, string? alias)
    {
        Assert.Equal(alias, SshConfigText.ToAlias(name));
    }

    [Theory]
    [InlineData("a-b.c_d", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("a b", false)]
    [InlineData("-x", false)]
    [InlineData("a*b", false)]
    [InlineData("user@host", false)]
    [InlineData("host:22", false)]
    [InlineData("", false)]
    public void Aliases_are_validated(string alias, bool valid)
    {
        Assert.Equal(valid, SshConfigText.IsValidAlias(alias));
    }

    [Fact]
    public void Values_are_split_like_ssh_does()
    {
        Assert.Equal(["a", "b c", "d"], SshConfigText.Split("a \"b c\" d"));
        Assert.Equal([@"C:\My Keys\id"], SshConfigText.Split("\"C:\\My Keys\\id\""));
        Assert.Empty(SshConfigText.Split("   "));
    }

    [Fact]
    public void Values_with_spaces_are_quoted()
    {
        Assert.Equal("\"a b\"", SshConfigText.QuoteValue("a b"));
        Assert.Equal("a", SshConfigText.QuoteValue("a"));
    }

    [Fact]
    public void Directives_accept_both_syntaxes()
    {
        Assert.Equal(("HostName", "10.0.0.5"), SshConfigText.SplitDirective("  HostName 10.0.0.5 "));
        Assert.Equal(("Port", "22"), SshConfigText.SplitDirective("Port=22"));
        Assert.Equal(("Port", "22"), SshConfigText.SplitDirective("Port = 22"));
    }

    [Fact]
    public void Paths_are_written_for_every_openssh_version()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal("~/.ssh/key", SshConfigText.NormalizePath(Path.Combine(home, ".ssh", "key")));
        Assert.Equal("~/.ssh/key", SshConfigText.NormalizePath(@"%USERPROFILE%\.ssh\key"));
        Assert.Equal("C:/Keys/id", SshConfigText.NormalizePath(@"""C:\Keys\id"""));
        Assert.Equal("~/.ssh/key", SshConfigText.NormalizePath("~/.ssh/key"));
    }
}
