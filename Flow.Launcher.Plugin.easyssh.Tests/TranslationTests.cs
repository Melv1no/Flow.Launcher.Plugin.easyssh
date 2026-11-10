using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using Flow.Launcher.Plugin.EasySsh.Core;

namespace Flow.Launcher.Plugin.EasySsh.Tests;

public partial class TranslationTests
{
    public static TheoryData<string> Languages => new() { "en", "fr" };

    [Theory]
    [MemberData(nameof(Languages))]
    public void Language_files_load_like_flow_launcher_loads_them(string language)
    {
        Sta.Run(() =>
        {
            using var stream = File.OpenRead(Repository.LanguageFile(language));
            var dictionary = Assert.IsType<ResourceDictionary>(XamlReader.Load(stream));
            Assert.Equal(Translations.Load(language).Count, dictionary.Count);
        });
    }

    [Fact]
    public void Every_language_has_the_same_keys()
    {
        Assert.Equal(Translations.English.Keys.Order(), Translations.Load("fr").Keys.Order());
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Placeholders_are_the_same_in_every_language(string language)
    {
        foreach (var (key, english) in Translations.English)
        {
            var translated = Translations.Load(language)[key];
            Assert.True(
                Placeholders(english).SetEquals(Placeholders(translated)),
                $"{language}/{key}: placeholders differ from English");
            Assert.False(translated.StartsWith('{'), $"{language}/{key} starts with '{{' (read as a XAML markup extension)");
        }
    }

    [Fact]
    public void Every_key_used_by_the_code_exists()
    {
        var sources = Directory
            .EnumerateFiles(Repository.PluginProject, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".xaml", StringComparison.Ordinal))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "Languages" + Path.DirectorySeparatorChar)
                           && !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                           && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

        var used = sources
            .SelectMany(path => KeyPattern().Matches(File.ReadAllText(path)).Select(match => match.Value))
            .Where(key => !key.EndsWith('_')) // prefixes of keys built at runtime, checked below
            .ToHashSet();

        // Keys built at runtime.
        used.UnionWith(BuiltInTerminals.All.Select(terminal => "plugin_easyssh_terminal_" + terminal.Name));
        foreach (var verb in new[] { "add", "edit", "remove", "profiles", "direct", "terminal", "config" })
        {
            used.Add($"plugin_easyssh_cmd_{verb}_title");
            used.Add($"plugin_easyssh_cmd_{verb}_subtitle");
        }

        Assert.Empty(used.Where(key => !Translations.English.ContainsKey(key)).Order());
    }

    private static HashSet<string> Placeholders(string text) =>
        PlaceholderPattern().Matches(text).Select(match => match.Value).ToHashSet();

    [GeneratedRegex("plugin_easyssh_[a-z0-9_]+")]
    private static partial Regex KeyPattern();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderPattern();
}
