using System.Text;
using Flow.Launcher.Plugin.EasySsh.Core;
using Flow.Launcher.Plugin.EasySsh.Storage;

namespace Flow.Launcher.Plugin.EasySsh.Tests.Storage;

public class SshConfigFilesTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    private string MainConfig => _temp.Combine(".ssh", "config");

    public void Dispose() => _temp.Dispose();

    private SshConfigFiles Files() => new(MainConfig);

    private static ManagedConfig WithHost(string alias, string hostName)
    {
        var config = ManagedConfig.CreateEmpty();
        config.Upsert(new SshHostEntry(alias, [new SshOption("HostName", hostName)]));
        return config;
    }

    [Fact]
    public void The_first_save_creates_the_folder_the_file_and_the_include()
    {
        var files = Files();

        Assert.True(files.Save(WithHost("prod", "10.0.0.5")));

        Assert.StartsWith("# Added by EasySSH", File.ReadAllText(MainConfig));
        Assert.Contains("\r\nInclude easyssh.config\r\n", File.ReadAllText(MainConfig));
        Assert.Equal("prod", Assert.Single(files.ReadManaged().Hosts).Alias);
        Assert.True(files.IsIncluded());
        Assert.Equal("prod", Assert.Single(SshConfigParser.Load(MainConfig).Hosts).Alias);
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void The_include_goes_on_top_and_the_rest_of_the_file_is_untouched(string newLine)
    {
        var original = $"# my config{newLine}Host *{newLine}  ServerAliveInterval 60{newLine}Host web{newLine}  HostName 10.0.0.9{newLine}";
        _temp.Write(Path.Combine(".ssh", "config"), original);

        Files().Save(WithHost("prod", "10.0.0.5"));

        var text = File.ReadAllText(MainConfig);
        Assert.EndsWith(newLine + newLine + original, text);
        Assert.Contains(newLine + "Include easyssh.config" + newLine, text);
        Assert.DoesNotContain(newLine == "\n" ? "\r\n" : "\r\r", text);
        Assert.Equal(original, File.ReadAllText(MainConfig + ".easyssh.bak"));

        // ssh reads the included hosts first: they never lose against a "Host *" block written above them.
        Assert.Equal(["prod", "web"], SshConfigParser.Load(MainConfig).Hosts.Select(host => host.Alias));
    }

    [Fact]
    public void A_byte_order_mark_is_kept_at_the_start()
    {
        var path = _temp.Write(Path.Combine(".ssh", "config"), string.Empty);
        File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("Host web\n")]);

        Files().Save(WithHost("prod", "10.0.0.5"));

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'#' }, bytes[..4]);
        Assert.Equal(1, Encoding.UTF8.GetString(bytes).Split('﻿').Length - 1);
    }

    [Theory]
    [InlineData("Include easyssh.config")]
    [InlineData("include=easyssh.config")]
    [InlineData("Include other.conf easyssh.config")]
    [InlineData("Include *.config")]
    [InlineData("Include \"{0}\"")]
    public void An_existing_include_is_not_added_again(string includeLine)
    {
        var line = string.Format(includeLine, _temp.Combine(".ssh", "easyssh.config").Replace('\\', '/'));
        var original = $"{line}\nHost web\n";
        _temp.Write(Path.Combine(".ssh", "config"), original);

        Assert.False(Files().Save(WithHost("prod", "10.0.0.5")));

        Assert.Equal(original, File.ReadAllText(MainConfig));
    }

    [Fact]
    public void An_include_inside_a_block_does_not_count()
    {
        _temp.Write(Path.Combine(".ssh", "config"), "Host web\n  Include easyssh.config\n");

        Assert.True(Files().Save(WithHost("prod", "10.0.0.5")));

        Assert.StartsWith("# Added by EasySSH", File.ReadAllText(MainConfig));
    }

    [Fact]
    public void The_previous_managed_file_is_kept_as_backup()
    {
        var files = Files();
        files.Save(WithHost("first", "1.1.1.1"));
        files.Save(WithHost("second", "2.2.2.2"));

        Assert.Contains("Host first", File.ReadAllText(files.ManagedPath + ".bak"));
        Assert.False(File.Exists(files.ManagedPath + ".tmp"));
    }

    [Fact]
    public void Managed_paths_are_recognized()
    {
        var files = Files();

        Assert.True(files.IsManaged(_temp.Combine(".ssh", "EASYSSH.CONFIG")));
        Assert.False(files.IsManaged(MainConfig));
        Assert.False(files.IsIncluded());
        Assert.Empty(files.ReadManaged().Hosts);
    }

    [Fact]
    public void Bytes_of_any_encoding_are_kept_after_the_include()
    {
        var path = _temp.Write(Path.Combine(".ssh", "config"), string.Empty);
        byte[] original = [.. Encoding.ASCII.GetBytes("# caf"), 0xE9, .. Encoding.ASCII.GetBytes(" (ANSI)\r\nHost web\r\n")];
        File.WriteAllBytes(path, original);

        Files().Save(WithHost("prod", "10.0.0.5"));

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(original, bytes[^original.Length..]);
        Assert.StartsWith("# Added by EasySSH", Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void A_utf16_config_is_reported_before_anything_is_written()
    {
        var path = _temp.Write(Path.Combine(".ssh", "config"), string.Empty);
        byte[] original = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("Host web\r\n")];
        File.WriteAllBytes(path, original);
        var files = Files();

        Assert.Throws<IOException>(() => files.Save(WithHost("prod", "10.0.0.5")));

        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.False(File.Exists(files.ManagedPath));
    }

    [Fact]
    public void Ssh_refuses_a_bad_option_and_nothing_is_written()
    {
        var files = Files();
        var config = ManagedConfig.CreateEmpty();
        config.Upsert(new SshHostEntry("bad", [new SshOption("NoSuchKeyword", "x")]));

        var error = Assert.Throws<SshConfigRejectedException>(() => files.Save(config));

        Assert.Contains("nosuchkeyword", error.SshMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("easyssh-check", error.SshMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(files.ManagedPath));
        Assert.False(File.Exists(MainConfig));
        Assert.Null(files.Validate(new SshHostEntry("good", [new SshOption("User", "\"o'neil\"")])));
    }
}
