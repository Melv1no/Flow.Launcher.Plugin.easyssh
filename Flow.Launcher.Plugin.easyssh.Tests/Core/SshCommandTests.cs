using Flow.Launcher.Plugin.EasySsh.Core;

namespace Flow.Launcher.Plugin.EasySsh.Tests.Core;

public class SshCommandTests
{
    [Theory]
    // Arguments only (EasySSH 1.x and the README syntax "ssh d root@127.0.0.1", issue #9).
    [InlineData("root@10.0.0.5", "ssh root@10.0.0.5")]
    [InlineData("root@host -p 2222", "ssh root@host -p 2222")]
    [InlineData("-p 22 host", "ssh -p 22 host")]
    [InlineData("myserver", "ssh myserver")]
    [InlineData("ssh://user@host:2222", "ssh ssh://user@host:2222")]
    // Full commands are kept.
    [InlineData("ssh root@host", "ssh root@host")]
    [InlineData("SSH.exe root@host", "SSH.exe root@host")]
    [InlineData("wsl ssh host", "wsl ssh host")]
    [InlineData("mosh admin@host", "mosh admin@host")]
    [InlineData(@"C:\tools\plink.exe -ssh host", @"C:\tools\plink.exe -ssh host")]
    [InlineData(@"""C:\Program Files\PuTTY\putty.exe"" -load prod", @"""C:\Program Files\PuTTY\putty.exe"" -load prod")]
    [InlineData("kubectl.exe exec -it pod -- bash", "kubectl.exe exec -it pod -- bash")]
    // Blank input.
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Normalize_adds_ssh_only_when_no_program_is_given(string? input, string expected)
    {
        Assert.Equal(expected, SshCommand.Normalize(input));
    }

    [Fact]
    public void Normalize_fixes_the_single_quoted_key_of_issue_4()
    {
        var command = SshCommand.Normalize(@"ssh -i 'C:\Users\me\.ssh\id_rsa' root@10.0.0.10 -p22222");

        Assert.Equal(@"ssh -i ""C:\Users\me\.ssh\id_rsa"" root@10.0.0.10 -p22222", command);
    }

    [Theory]
    [InlineData("ssh -i '~/.ssh/id' host", "ssh -i \"~/.ssh/id\" host")]
    [InlineData(@"ssh -F '%USERPROFILE%\.ssh\other' host", @"ssh -F ""%USERPROFILE%\.ssh\other"" host")]
    [InlineData("ssh host 'echo $HOME'", "ssh host 'echo $HOME'")]
    [InlineData("ssh host 'ls /tmp'", "ssh host 'ls /tmp'")]
    public void FixSingleQuotedPaths_only_touches_local_paths(string input, string expected)
    {
        Assert.Equal(expected, SshCommand.FixSingleQuotedPaths(input));
    }

    [Theory]
    [InlineData("ssh host", true)]
    [InlineData("scp a b:", true)]
    [InlineData("sftp host", true)]
    [InlineData(@"C:\Windows\System32\OpenSSH\ssh.exe host", true)]
    [InlineData("wsl ssh host", false)]
    [InlineData("plink -ssh host", false)]
    public void UsesOpenSsh_detects_openssh_programs(string command, bool expected)
    {
        Assert.Equal(expected, SshCommand.UsesOpenSsh(command));
    }

    [Theory]
    [InlineData("ssh -p 2222 root@10.0.0.5", "10.0.0.5")]
    [InlineData("ssh -i \"C:\\k k\" admin@web", "web")]
    [InlineData("ssh -o StrictHostKeyChecking=no host", "host")]
    [InlineData("ssh -v -A bastion", "bastion")]
    [InlineData("ssh ssh://user@host:2222", "host")]
    [InlineData("ssh ssh://[::1]:22", "[::1]")]
    [InlineData("root@db", "db")]
    [InlineData("ssh", null)]
    [InlineData("ssh -p 22", null)]
    public void GuessDestination_finds_the_host(string command, string? expected)
    {
        Assert.Equal(expected, SshCommand.GuessDestination(command));
    }
}
