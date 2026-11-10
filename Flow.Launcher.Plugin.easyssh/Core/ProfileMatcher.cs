namespace Flow.Launcher.Plugin.EasySsh.Core;

/// <summary>A parsed search: whitespace-separated terms, already folded.</summary>
public sealed class SearchQuery
{
    private SearchQuery(string text, IReadOnlyList<string> terms)
    {
        Text = text;
        Terms = terms;
    }

    /// <summary>The search as typed (trimmed).</summary>
    public string Text { get; }

    /// <summary>Folded terms; a candidate must match all of them.</summary>
    public IReadOnlyList<string> Terms { get; }

    /// <summary>Whether there is nothing to search for.</summary>
    public bool IsEmpty => Terms.Count == 0;

    /// <summary>Parses a search string.</summary>
    public static SearchQuery Parse(string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        var terms = trimmed
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(TextFolding.Fold)
            .ToArray();
        return new SearchQuery(trimmed, terms);
    }
}

/// <summary>How well a candidate matches a <see cref="SearchQuery"/>.</summary>
/// <param name="Tier">Relevance tier, 0 is best (see <see cref="ProfileMatcher"/>).</param>
/// <param name="NameStartsWithQuery">Whether the name starts with the first term.</param>
/// <param name="NameHighlights">Indexes of the name characters to highlight.</param>
public sealed record MatchResult(int Tier, bool NameStartsWithQuery, IReadOnlyList<int> NameHighlights)
{
    /// <summary>Result used when the search is empty: everything matches.</summary>
    public static MatchResult Everything { get; } = new(0, false, []);
}

/// <summary>
/// Matches a name and a command against search terms. Every term must be found, exactly or with typos,
/// in the name or in the command. Results are ranked in tiers:
/// <list type="number">
/// <item>exact match in name and command,</item>
/// <item>exact match in name,</item>
/// <item>exact match in command,</item>
/// <item>approximate match in name and command,</item>
/// <item>approximate match in name,</item>
/// <item>anything else that matched.</item>
/// </list>
/// Matching ignores case and accents and tolerates one typo every five characters
/// (substitution, insertion, deletion or swap of two adjacent characters).
/// </summary>
public static class ProfileMatcher
{
    /// <summary>Number of relevance tiers.</summary>
    public const int TierCount = 6;

    /// <summary>Returns how <paramref name="name"/>/<paramref name="command"/> match, or null when they don't.</summary>
    public static MatchResult? Match(SearchQuery query, string name, string command)
    {
        if (query.IsEmpty)
        {
            return MatchResult.Everything;
        }

        var foldedName = TextFolding.Fold(name);
        var foldedCommand = TextFolding.Fold(command);
        bool exactName = true, exactCommand = true, nearName = true, nearCommand = true;
        var highlights = new SortedSet<int>();

        foreach (var term in query.Terms)
        {
            var nameIndex = foldedName.IndexOf(term, StringComparison.Ordinal);
            var inName = nameIndex >= 0;
            var inCommand = foldedCommand.Contains(term, StringComparison.Ordinal);
            var closeToName = inName || FuzzyContains(foldedName, term);
            var closeToCommand = inCommand || FuzzyContains(foldedCommand, term);
            if (!closeToName && !closeToCommand)
            {
                return null;
            }

            exactName &= inName;
            exactCommand &= inCommand;
            nearName &= closeToName;
            nearCommand &= closeToCommand;

            if (inName)
            {
                for (var i = 0; i < term.Length; i++)
                {
                    highlights.Add(nameIndex + i);
                }
            }
        }

        var tier = (exactName, exactCommand, nearName, nearCommand) switch
        {
            (true, true, _, _) => 0,
            (true, false, _, _) => 1,
            (false, true, _, _) => 2,
            (_, _, true, true) => 3,
            (_, _, true, false) => 4,
            _ => 5,
        };

        var startsWith = foldedName.StartsWith(query.Terms[0], StringComparison.Ordinal);
        return new MatchResult(tier, startsWith, highlights.ToArray());
    }

    /// <summary>Number of typos tolerated for a term: one every five characters.</summary>
    public static int AllowedTypos(int termLength) => termLength / 5;

    /// <summary>
    /// Whether <paramref name="foldedText"/> contains a substring within <see cref="AllowedTypos"/> edits
    /// of <paramref name="foldedTerm"/> (optimal string alignment distance, i.e. Damerau-Levenshtein
    /// with adjacent transpositions). Uses Sellers' algorithm: one dynamic-programming pass where the
    /// match may start anywhere in the text.
    /// </summary>
    public static bool FuzzyContains(string foldedText, string foldedTerm)
    {
        var termLength = foldedTerm.Length;
        if (termLength == 0)
        {
            return true;
        }

        var maxTypos = AllowedTypos(termLength);
        if (maxTypos == 0)
        {
            return foldedText.Contains(foldedTerm, StringComparison.Ordinal);
        }

        var textLength = foldedText.Length;
        if (textLength < termLength - maxTypos)
        {
            return false;
        }

        // Rows follow the term, columns follow the text. Row 0 is all zeros: a match may start anywhere.
        var twoRowsAgo = new int[textLength + 1];
        var previous = new int[textLength + 1];
        var current = new int[textLength + 1];

        for (var i = 1; i <= termLength; i++)
        {
            current[0] = i;
            for (var j = 1; j <= textLength; j++)
            {
                var cost = foldedTerm[i - 1] == foldedText[j - 1] ? 0 : 1;
                var distance = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
                if (i > 1 && j > 1
                    && foldedTerm[i - 1] == foldedText[j - 2]
                    && foldedTerm[i - 2] == foldedText[j - 1])
                {
                    distance = Math.Min(distance, twoRowsAgo[j - 2] + 1);
                }

                current[j] = distance;
            }

            (twoRowsAgo, previous, current) = (previous, current, twoRowsAgo);
        }

        for (var j = 1; j <= textLength; j++)
        {
            if (previous[j] <= maxTypos)
            {
                return true;
            }
        }

        return false;
    }
}
