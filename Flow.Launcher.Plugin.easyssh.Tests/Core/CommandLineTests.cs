using Flow.Launcher.Plugin.EasySsh.Core;

namespace Flow.Launcher.Plugin.EasySsh.Tests.Core;

public class CommandLineTests
{
    [Fact]
    public void Tokenize_splits_on_whitespace()
    {
        var tokens = CommandLine.Tokenize("ssh  -p 22\troot@host");

        Assert.Equal(["ssh", "-p", "22", "root@host"], tokens.Select(token => token.Value));
    }

    [Fact]
    public void Tokenize_keeps_quoted_spaces_and_records_positions()
    {
        const string text = "ssh -i \"C:\\My Keys\\id\" host";

        var tokens = CommandLine.Tokenize(text);

        Assert.Equal(["ssh", "-i", @"C:\My Keys\id", "host"], tokens.Select(token => token.Value));
        Assert.Equal("\"C:\\My Keys\\id\"", text[tokens[2].Start..tokens[2].End]);
    }

    [Theory]
    [InlineData("say \\\"hi\\\"", new[] { "say", "\"hi\"" })]
    [InlineData("\"C:\\dir\\\\\" next", new[] { "C:\\dir\\", "next" })]
    [InlineData("a\\b \"x\"y", new[] { "a\\b", "xy" })]
    [InlineData("", new string[0])]
    [InlineData("   ", new string[0])]
    public void Tokenize_follows_windows_quoting_rules(string text, string[] expected)
    {
        Assert.Equal(expected, CommandLine.Tokenize(text).Select(token => token.Value));
    }

    [Fact]
    public void SplitFirst_keeps_the_rest_verbatim()
    {
        var (first, rest) = CommandLine.SplitFirst("  \"My Server\"   ssh -i \"C:\\k k\" host  ");

        Assert.Equal("My Server", first);
        Assert.Equal("ssh -i \"C:\\k k\" host", rest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void SplitFirst_of_nothing_is_empty(string? text)
    {
        Assert.Equal((string.Empty, string.Empty), CommandLine.SplitFirst(text));
    }

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData("C:\\my dir\\", "\"C:\\my dir\\\\\"")]
    [InlineData("", "\"\"")]
    public void Quote_produces_a_single_argument(string value, string expected)
    {
        Assert.Equal(expected, CommandLine.Quote(value));
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("with space")]
    [InlineData("quote\"inside")]
    [InlineData("C:\\trailing\\")]
    [InlineData("C:\\with space\\")]
    [InlineData("back\\\\\"slash")]
    public void Quote_round_trips_through_Tokenize(string value)
    {
        var tokens = CommandLine.Tokenize(CommandLine.Quote(value));

        Assert.Equal(value, Assert.Single(tokens).Value);
    }
}
