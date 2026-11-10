using Flow.Launcher.Plugin.EasySsh.Core;

namespace Flow.Launcher.Plugin.EasySsh.Tests.Core;

public class SshCommandConverterTests
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static IReadOnlyList<string> Options(string alias, string command)
    {
        var conversion = SshCommandConverter.ToHost(alias, command);
        Assert.True(conversion.Succeeded, $"Not converted: {conversion.Unsupported}");
        return conversion.Host!.Options.Select(option => option.ToString()).ToList();
    }

    [Fact]
    public void A_destination_alone_gives_host_name_and_user()
    {
        Assert.Equal(["HostName 10.0.0.5", "User admin"], Options("prod", "admin@10.0.0.5"));
    }

    [Fact]
    public void A_full_command_gives_every_option()
    {
        var key = Path.Combine(Home, ".ssh", "my key");

        var options = Options("prod", $"ssh -p 2222 -i \"{key}\" -J bastion admin@10.0.0.5");

        Assert.Equal(["HostName 10.0.0.5", "User admin", "Port 2222", "IdentityFile \"~/.ssh/my key\"", "ProxyJump bastion"], options);
    }

    [Fact]
    public void Options_after_the_destination_are_read_as_ssh_does()
    {
        // The format of EasySSH 1.x profiles: "root@host -p 22".
        Assert.Equal(["HostName host", "User root", "Port 22"], Options("h", "root@host -p 22"));
    }

    [Theory]
    [InlineData("-l first second@host", "User first")]
    [InlineData("second@host -l first", "User second")]
    [InlineData("-p 1 -p 2 host", "Port 1")]
    public void The_first_value_wins_like_on_the_command_line(string command, string expected)
    {
        Assert.Contains(expected, Options("h", command));
    }

    [Fact]
    public void Grouped_flags_and_attached_values_are_split()
    {
        Assert.Equal(["HostName host", "Port 2222", "ForwardAgent yes", "ForwardX11 yes", "LogLevel DEBUG2"], Options("h", "-AX -p2222 -vv host"));
    }

    [Fact]
    public void Forwards_get_the_ssh_config_syntax()
    {
        var options = Options("tunnel", "-L 8080:localhost:80 -L 127.0.0.1:9000:db:5432 -L [::1]:7000:web:443 -R 2222 -D 1080 host");

        Assert.Equal(
            [
                "HostName host",
                "LocalForward 8080 localhost:80",
                "LocalForward 127.0.0.1:9000 db:5432",
                "LocalForward [::1]:7000 web:443",
                "RemoteForward 2222",
                "DynamicForward 1080",
            ],
            options);
    }

    [Fact]
    public void Dash_o_options_are_kept_and_quoted_when_needed()
    {
        var options = Options("h", "-o ServerAliveInterval=30 -o \"ProxyCommand ssh -W %h:%p bastion\" -o \"IdentityFile=C:\\My Keys\\id\" host");

        Assert.Equal(["HostName host", "ServerAliveInterval 30", "ProxyCommand ssh -W %h:%p bastion", "IdentityFile \"C:/My Keys/id\""], options);
    }

    [Fact]
    public void A_remote_command_becomes_remote_command()
    {
        Assert.Equal(["HostName host", "RequestTTY yes", "RemoteCommand htop"], Options("h", "-t host htop"));
        Assert.Contains("RemoteCommand date +%%s", Options("h", "host date +%s"));
        Assert.Contains("RemoteCommand tmux attach -d", Options("h", "host tmux attach -d"));
    }

    [Fact]
    public void Ssh_uris_are_understood()
    {
        Assert.Equal(["HostName host", "User admin", "Port 2200"], Options("h", "ssh://admin@host:2200"));
        Assert.Equal(["HostName ::1", "Port 22"], Options("h", "ssh://[::1]:22"));
    }

    [Fact]
    public void The_host_name_is_omitted_when_it_is_the_alias()
    {
        Assert.Equal(["User git"], Options("github.com", "git@github.com"));
    }

    [Theory]
    [InlineData("ssh -N -L 8080:localhost:80 host", "-N")]
    [InlineData("ssh -f host", "-f")]
    [InlineData("ssh -W bastion:22 host", "-W")]
    [InlineData("ssh -F other.config host", "-F")]
    [InlineData("wsl ssh host", "wsl")]
    [InlineData(@"C:\Git\usr\bin\ssh.exe host", @"C:\Git\usr\bin\ssh.exe")]
    [InlineData("ssh -o HostName=other host", "-o HostName=other")]
    [InlineData("ssh -p port host", "-p port")]
    [InlineData("ssh host echo \"#hash\"", "echo #hash")]
    [InlineData("ssh host echo 'hi'", "echo 'hi'")]
    [InlineData("ssh -l a%b host", "-l a%b")]
    public void What_ssh_config_cannot_express_is_reported(string command, string unsupported)
    {
        var conversion = SshCommandConverter.ToHost("h", command);

        Assert.False(conversion.Succeeded);
        Assert.Equal(unsupported, conversion.Unsupported);
    }

    [Fact]
    public void An_invalid_alias_is_reported_with_the_destination()
    {
        var conversion = SshCommandConverter.ToHost("my server", "admin@host");

        Assert.False(conversion.Succeeded);
        Assert.Equal("host", conversion.Destination);
    }

    [Theory]
    [InlineData("admin@10.0.0.5 -p 2222")]
    [InlineData("ssh -i ~/.ssh/key -J bastion -L 8080:localhost:80 -o ServerAliveInterval=30 -A admin@web")]
    [InlineData("ssh -t root@host tmux attach")]
    [InlineData("ssh -o \"ProxyCommand ssh -W %h:%p jump\" -R 9000:localhost:9000 host")]
    public void Hosts_can_be_written_back_as_arguments(string command)
    {
        var host = SshCommandConverter.ToHost("prod", command).Host!;

        var arguments = SshCommandConverter.ToArguments(host);

        Assert.Equal(host, SshCommandConverter.ToHost("prod", arguments).Host);
    }

    [Fact]
    public void Arguments_are_short_and_readable()
    {
        var host = SshCommandConverter.ToHost("prod", "ssh -p 2222 -L 8080:localhost:80 admin@10.0.0.5").Host!;

        Assert.Equal("ssh admin@10.0.0.5 -p 2222 -L 8080:localhost:80", SshCommandConverter.ToArguments(host));
    }

    [Theory]
    [InlineData("22", true)]
    [InlineData("65535", true)]
    [InlineData("0", false)]
    [InlineData("65536", false)]
    [InlineData("-1", false)]
    [InlineData("2a", false)]
    public void Ports_are_validated(string port, bool valid)
    {
        Assert.Equal(valid, SshCommandConverter.IsPort(port));
    }

    [Fact]
    public void Percent_signs_of_the_host_name_are_escaped()
    {
        var host = SshCommandConverter.ToHost("h", "admin@fe80::1%12").Host!;

        Assert.Contains(new SshOption("HostName", "fe80::1%%12"), host.Options);
        Assert.Equal("ssh admin@fe80::1%12", SshCommandConverter.ToArguments(host));
    }

    [Fact]
    public void Single_quotes_and_hashes_are_double_quoted_for_recent_openssh()
    {
        Assert.Contains("User \"o'neil\"", Options("h", "-l o'neil host"));
        Assert.Contains("IdentityFile \"D:/k/o'brien.pem\"", Options("h", "-i \"D:\\k\\o'brien.pem\" host"));
        Assert.Contains("User \"a#b\"", Options("h", "-l a#b host"));
    }

    [Fact]
    public void An_empty_remote_command_opens_a_shell_as_without_command()
    {
        Assert.Equal(["HostName host"], Options("h", "ssh host \"\""));
    }

    [Fact]
    public void Written_back_hosts_start_with_ssh_so_a_dot_com_host_stays_a_host()
    {
        var host = SshCommandConverter.ToHost("web", "ssh -p 2222 web.example.com").Host!;

        var arguments = SshCommandConverter.ToArguments(host);

        Assert.Equal("ssh web.example.com -p 2222", arguments);
        Assert.Equal(host, SshCommandConverter.ToHost("web", arguments).Host);
    }
}
