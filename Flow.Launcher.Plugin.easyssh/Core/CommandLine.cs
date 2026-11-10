using System.Text;

namespace Flow.Launcher.Plugin.EasySsh.Core;

/// <summary>A token of a command line and its position in the source text.</summary>
/// <param name="Value">The token with quotes removed and escapes resolved.</param>
/// <param name="Start">Index of the first character of the token in the source text.</param>
/// <param name="End">Index just after the last character of the token in the source text.</param>
public readonly record struct CommandToken(string Value, int Start, int End);

/// <summary>
/// Windows command line helpers. Quoting follows the <c>CommandLineToArgvW</c> rules:
/// double quotes group words, and backslashes only escape a following double quote.
/// </summary>
public static class CommandLine
{
    /// <summary>Splits <paramref name="text"/> into tokens.</summary>
    public static IReadOnlyList<CommandToken> Tokenize(string? text)
    {
        var tokens = new List<CommandToken>();
        if (string.IsNullOrEmpty(text))
        {
            return tokens;
        }

        var index = 0;
        while (TryReadToken(text, ref index, out var token))
        {
            tokens.Add(token);
        }

        return tokens;
    }

    /// <summary>
    /// Returns the first token (unquoted) and the rest of the text, kept verbatim
    /// so that the quoting of the remaining arguments is preserved.
    /// </summary>
    public static (string First, string Remainder) SplitFirst(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (string.Empty, string.Empty);
        }

        var index = 0;
        if (!TryReadToken(text, ref index, out var token))
        {
            return (string.Empty, string.Empty);
        }

        return (token.Value, text[token.End..].Trim());
    }

    /// <summary>Wraps <paramref name="value"/> in double quotes when it contains whitespace or quotes.</summary>
    public static string Quote(string value)
    {
        if (value.Length > 0 && value.IndexOfAny([' ', '\t', '"']) < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 2).Append('"');
        var backslashes = 0;
        foreach (var c in value)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                builder.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                builder.Append('\\', backslashes).Append(c);
            }

            backslashes = 0;
        }

        return builder.Append('\\', backslashes * 2).Append('"').ToString();
    }

    private static bool TryReadToken(string text, ref int index, out CommandToken token)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        if (index >= text.Length)
        {
            token = default;
            return false;
        }

        var start = index;
        var value = new StringBuilder();
        var inQuotes = false;
        while (index < text.Length)
        {
            var c = text[index];
            if (c == '\\')
            {
                var backslashes = 0;
                while (index < text.Length && text[index] == '\\')
                {
                    backslashes++;
                    index++;
                }

                if (index < text.Length && text[index] == '"')
                {
                    value.Append('\\', backslashes / 2);
                    if (backslashes % 2 == 1)
                    {
                        value.Append('"');
                        index++;
                    }
                }
                else
                {
                    value.Append('\\', backslashes);
                }

                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                index++;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                break;
            }

            value.Append(c);
            index++;
        }

        token = new CommandToken(value.ToString(), start, index);
        return true;
    }
}
