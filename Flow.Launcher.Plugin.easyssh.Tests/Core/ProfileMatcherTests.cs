using Flow.Launcher.Plugin.EasySsh.Core;

namespace Flow.Launcher.Plugin.EasySsh.Tests.Core;

public class ProfileMatcherTests
{
    private static MatchResult? Match(string search, string name, string command) =>
        ProfileMatcher.Match(SearchQuery.Parse(search), name, command);

    [Fact]
    public void Empty_search_matches_everything()
    {
        Assert.NotNull(Match("  ", "anything", "ssh anything"));
    }

    [Fact]
    public void Matching_ignores_case_and_accents()
    {
        Assert.NotNull(Match("preprod", "Préprod-web", "ssh web@preprod"));
        Assert.NotNull(Match("PRÉPROD", "preprod-web", "ssh web@preprod"));
    }

    // Examples of pull request #5.
    [Theory]
    [InlineData("prod", "site_prod", true)]
    [InlineData("prod", "site_staging", true)] // "preprod" is in its command
    [InlineData("site stag", "site_staging", true)]
    [InlineData("site stag", "site_prod", false)]
    [InlineData("preprod", "site_prod", false)]
    public void All_terms_must_match_the_name_or_the_command(string search, string name, bool expected)
    {
        var command = name == "site_prod" ? "ssh user@site.com" : "ssh user@preprod.site.com";

        Assert.Equal(expected, Match(search, name, command) is not null);
    }

    [Theory]
    [InlineData("prod", "prod", "ssh prod.example.com", 0)]
    [InlineData("prod", "prod", "ssh 10.0.0.1", 1)]
    [InlineData("prod", "web", "ssh prod.example.com", 2)]
    [InlineData("servrr", "server", "ssh server.example.com", 3)]
    [InlineData("servrr", "server", "ssh 10.0.0.1", 4)]
    [InlineData("servrr", "web", "ssh server.example.com", 5)]
    [InlineData("web prod", "web", "ssh admin@prod", 5)]
    public void Results_are_ranked_in_tiers(string search, string name, string command, int tier)
    {
        Assert.Equal(tier, Match(search, name, command)?.Tier);
    }

    // Examples of the README (pull request #8): one typo every five characters.
    [Theory]
    [InlineData("servrr")] // substitution
    [InlineData("serrver")] // insertion
    [InlineData("servr")] // deletion
    [InlineData("servre")] // swap of two adjacent characters
    [InlineData("esrver")] // swap at the start
    public void One_typo_is_tolerated_in_five_characters(string term)
    {
        Assert.True(ProfileMatcher.FuzzyContains("my-server-01", term));
    }

    [Theory]
    [InlineData("srvr")] // 4 characters: exact only
    [InlineData("svrrr")] // 2 typos in 5 characters
    public void Short_or_too_different_terms_do_not_match(string term)
    {
        Assert.False(ProfileMatcher.FuzzyContains("my-server-01", term));
    }

    [Fact]
    public void Ten_characters_allow_two_typos()
    {
        Assert.True(ProfileMatcher.FuzzyContains("production-database", "prodcutoin"));
        Assert.False(ProfileMatcher.FuzzyContains("production-database", "pordcutoin"));
    }

    [Fact]
    public void Exact_matches_of_the_name_are_highlighted()
    {
        Assert.Equal([5, 6, 7], Match("web", "Prod-Web", "ssh x")!.NameHighlights);
        Assert.Equal([0, 1, 2, 8, 9, 10], Match("pré web", "Preprod web", "ssh x")!.NameHighlights);
    }

    [Fact]
    public void Prefix_of_the_name_is_reported()
    {
        Assert.True(Match("web", "web-01", "ssh x")!.NameStartsWithQuery);
        Assert.False(Match("web", "preprod-web", "ssh x")!.NameStartsWithQuery);
    }

    [Fact]
    public void Quotes_and_extra_spaces_are_not_terms()
    {
        var query = SearchQuery.Parse("  web   prod ");

        Assert.Equal(["web", "prod"], query.Terms);
    }
}
