using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Flow.Launcher.Plugin.EasySsh.Core;

/// <summary>
/// Case- and accent-insensitive text folding ("Préprod" → "preprod").
/// Each character is folded on its own so indexes in the folded text match the original text,
/// which keeps search highlights aligned.
/// </summary>
public static class TextFolding
{
    private static readonly ConcurrentDictionary<char, char> Cache = new();

    /// <summary>Folds a whole string, keeping its length.</summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = Fold(source[i]);
            }
        });
    }

    /// <summary>Folds a single character.</summary>
    public static char Fold(char c)
    {
        if (c < 128)
        {
            return char.ToLowerInvariant(c);
        }

        if (char.IsSurrogate(c))
        {
            return c;
        }

        return Cache.GetOrAdd(c, static value =>
        {
            foreach (var part in value.ToString().Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(part) != UnicodeCategory.NonSpacingMark)
                {
                    return char.ToLowerInvariant(part);
                }
            }

            return char.ToLowerInvariant(value);
        });
    }
}
