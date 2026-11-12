using System.Globalization;
using System.Text;

namespace Flow.Launcher.Plugin.EasySsh.Core;

/// <summary>
/// One line of a <c>Host</c> block: <c>Keyword value</c>. <see cref="Value"/> is the text written after the keyword,
/// quotes included (<c>"C:/My Keys/id"</c>, <c>8080 localhost:80</c>).
/// </summary>
public sealed record SshOption(string Keyword, string Value)
{
    /// <summary>An option with a single value, quoted when it contains whitespace.</summary>
    public static SshOption Single(string keyword, string value) => new(keyword, SshConfigText.QuoteValue(value));

    /// <summary>The value without its quotes (first token only, which is what single-value keywords use).</summary>
    public string FirstValue => SshConfigText.FirstToken(Value) ?? string.Empty;

    /// <inheritdoc />
    public override string ToString() => Keyword + " " + Value;
}

/// <summary>A host of an ssh_config file: its alias and its options, in the order they are written.</summary>
public sealed class SshHostEntry : IEquatable<SshHostEntry>
{
    /// <summary>Creates a host.</summary>
    public SshHostEntry(string alias, IEnumerable<SshOption> options)
    {
        Alias = alias;
        Options = options.ToArray();
    }

    /// <summary>The name given to ssh (<c>ssh prod</c>).</summary>
    public string Alias { get; }

    /// <summary>The options of the block.</summary>
    public IReadOnlyList<SshOption> Options { get; }

    /// <summary>Real host name, when different from the alias.</summary>
    public string? HostName => Get("HostName");

    /// <summary>User name.</summary>
    public string? User => Get("User");

    /// <summary>Port.</summary>
    public string? Port => Get("Port");

    /// <summary>First identity file.</summary>
    public string? IdentityFile => Get("IdentityFile");

    /// <summary>Jump host(s).</summary>
    public string? ProxyJump => Get("ProxyJump");

    /// <summary>The (unquoted) value of the first option named <paramref name="keyword"/>.</summary>
    public string? Get(string keyword) =>
        Options.FirstOrDefault(option => option.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase))?.FirstValue;

    /// <summary>A copy with another alias.</summary>
    public SshHostEntry WithAlias(string alias) => new(alias, Options);

    /// <inheritdoc />
    public bool Equals(SshHostEntry? other) =>
        other is not null && Alias == other.Alias && Options.SequenceEqual(other.Options);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SshHostEntry);

    /// <inheritdoc />
    public override int GetHashCode() => Alias.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => $"Host {Alias} ({string.Join(", ", Options)})";
}

/// <summary>Text rules of ssh_config files.</summary>
public static class SshConfigText
{
    /// <summary>Keywords that start a new section: they can't appear among the options of a host.</summary>
    public static readonly IReadOnlySet<string> SectionKeywords =
        new HashSet<string>(["Host", "Match", "Include"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether <paramref name="alias"/> can be a host alias: one word, no pattern character, not an option,
    /// and nothing that ssh would read as a user (<c>@</c>) or a port (<c>:</c>).
    /// </summary>
    public static bool IsValidAlias(string? alias) =>
        !string.IsNullOrEmpty(alias)
        && alias[0] != '-'
        && alias.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    /// <summary>
    /// Turns a display name into an alias: accents removed, spaces replaced by dashes, other characters dropped
    /// (<c>"Préprod web"</c> gives <c>Preprod-web</c>). Returns null when nothing is left.
    /// </summary>
    public static string? ToAlias(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var builder = new StringBuilder(name.Length);
        var pendingDash = false;
        foreach (var c in name.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c) || c is '-' or '/' or '\\' or ':' or '@')
            {
                pendingDash = builder.Length > 0;
                continue;
            }

            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '_')
            {
                if (pendingDash)
                {
                    builder.Append('-');
                    pendingDash = false;
                }

                builder.Append(c);
            }
        }

        var alias = builder.ToString().Trim('.', '_');
        return IsValidAlias(alias) ? alias : null;
    }

    /// <summary>
    /// Wraps a value in double quotes when ssh would not read it as one word: whitespace, and since OpenSSH 8.7
    /// a single quote (a quote too) or a <c>#</c> (a comment). Values containing a double quote can't be written.
    /// </summary>
    public static string QuoteValue(string value) =>
        value.Length > 0 && value.IndexOfAny([' ', '\t', '\'', '#']) < 0 ? value : "\"" + value + "\"";

    /// <summary>The first word of a value, without its quotes.</summary>
    public static string? FirstToken(string? value)
    {
        var tokens = Split(value);
        return tokens.Count > 0 ? tokens[0] : null;
    }

    /// <summary>
    /// Splits a value into words the way ssh does for most keywords: whitespace separates words, double quotes
    /// group them. Backslashes are kept as they are (Windows paths).
    /// </summary>
    public static IReadOnlyList<string> Split(string? value)
    {
        var words = new List<string>();
        if (string.IsNullOrEmpty(value))
        {
            return words;
        }

        var current = new StringBuilder();
        var inQuotes = false;
        var hasWord = false;
        foreach (var c in value)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasWord = true;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (hasWord)
                {
                    words.Add(current.ToString());
                    current.Clear();
                    hasWord = false;
                }

                continue;
            }

            current.Append(c);
            hasWord = true;
        }

        if (hasWord)
        {
            words.Add(current.ToString());
        }

        return words;
    }

    /// <summary>
    /// Splits a configuration line into its keyword and the rest (<c>Keyword value</c> or <c>Keyword=value</c>).
    /// </summary>
    public static (string Keyword, string Value) SplitDirective(string line)
    {
        var trimmed = line.Trim();
        var end = 0;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]) && trimmed[end] != '=')
        {
            end++;
        }

        var rest = trimmed[end..].TrimStart();
        if (rest.StartsWith('='))
        {
            rest = rest[1..].TrimStart();
        }

        return (trimmed[..end], rest);
    }

    /// <summary>Whether a line is empty or a comment.</summary>
    public static bool IsTrivia(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.Length == 0 || trimmed[0] == '#';
    }

    /// <summary>
    /// Writes a path the way ssh_config reads it on every OpenSSH version: environment variables expanded,
    /// the user folder written <c>~</c>, and forward slashes (a backslash before a quote or another backslash
    /// is an escape for recent versions).
    /// </summary>
    public static string NormalizePath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 0
            && expanded.StartsWith(home, StringComparison.OrdinalIgnoreCase)
            && (expanded.Length == home.Length || expanded[home.Length] is '\\' or '/'))
        {
            expanded = "~" + expanded[home.Length..];
        }

        return expanded.Replace('\\', '/');
    }
}
