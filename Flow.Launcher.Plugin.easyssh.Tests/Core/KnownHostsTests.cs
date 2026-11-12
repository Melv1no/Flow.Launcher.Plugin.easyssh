using Flow.Launcher.Plugin.EasySsh.Core;

namespace Flow.Launcher.Plugin.EasySsh.Tests.Core;

public class KnownHostsTests
{
    [Fact]
    public void Lists_the_servers_once_with_their_port()
    {
        var hosts = KnownHostsParser.Parse(
        [
            "# comment",
            "github.com,140.82.121.4 ssh-ed25519 AAAA",
            "github.com ssh-rsa BBBB",
            "[10.0.0.5]:2222 ecdsa-sha2-nistp256 CCCC",
            "[10.0.0.6]:22 ssh-ed25519 DDDD",
            "|1|c2FsdA==|aGFzaA== ssh-ed25519 EEEE",
            "@cert-authority *.corp ssh-rsa FFFF",
            "*.wild ssh-rsa GGGG",
            "",
            "no-key-on-this-line",
        ]);

        Assert.Equal([new KnownHost("github.com", null), new KnownHost("10.0.0.5", "2222"), new KnownHost("10.0.0.6", null)], hosts);
        Assert.Equal("ssh -p 2222 10.0.0.5", hosts[1].Command);
        Assert.Equal("10.0.0.5:2222", hosts[1].Display);
    }

    [Fact]
    public void A_missing_file_has_no_server()
    {
        Assert.Empty(KnownHostsParser.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }
}
