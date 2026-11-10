using Flow.Launcher.Plugin.EasySsh.Core;

namespace Flow.Launcher.Plugin.EasySsh.Tests.Core;

public class TextFoldingTests
{
    [Theory]
    [InlineData("Préprod-ÉTÉ", "preprod-ete")]
    [InlineData("Ångström Çà", "angstrom ca")]
    [InlineData("SERVER-01", "server-01")]
    [InlineData("", "")]
    public void Fold_removes_case_and_accents(string input, string expected)
    {
        Assert.Equal(expected, TextFolding.Fold(input));
    }

    [Theory]
    [InlineData("Préprod-ÉTÉ")]
    [InlineData("straße Œuvre")]
    [InlineData("srv 🚀 prod")]
    public void Fold_keeps_the_length_so_highlights_stay_aligned(string input)
    {
        Assert.Equal(input.Length, TextFolding.Fold(input).Length);
    }

    [Fact]
    public void Fold_leaves_lone_surrogates_alone()
    {
        Assert.Equal("\uD83D", TextFolding.Fold("\uD83D"));
    }
}
