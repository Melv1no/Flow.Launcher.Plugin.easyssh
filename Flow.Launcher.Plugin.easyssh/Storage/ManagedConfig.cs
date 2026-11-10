using Flow.Launcher.Plugin.EasySsh.Core;

namespace Flow.Launcher.Plugin.EasySsh.Storage;

/// <summary>
/// The ssh_config file where EasySSH writes its hosts (<c>~/.ssh/easyssh.config</c>). It is edited block by block:
/// a host changed with EasySSH is rewritten in place, everything else (comments, blocks written by hand) is kept.
/// </summary>
public sealed class ManagedConfig
{
    /// <summary>Lines written at the top of a new file.</summary>
    public static readonly IReadOnlyList<string> Header =
    [
        "# Hosts saved by EasySSH, the Flow Launcher plugin. ~/.ssh/config includes this file.",
        "# You can edit it: EasySSH only rewrites the hosts you change with it.",
    ];

    private const string Indent = "    ";

    private readonly List<string> _lines;

    private ManagedConfig(List<string> lines)
    {
        _lines = lines;
    }

    /// <summary>A new file with only the header.</summary>
    public static ManagedConfig CreateEmpty() => new([.. Header]);

    /// <summary>Parses the content of a file.</summary>
    public static ManagedConfig Parse(string text)
    {
        if (text.Length > 0 && text[0] == '﻿')
        {
            // A byte order mark breaks ssh: it is dropped on the next save.
            text = text[1..];
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return new ManagedConfig(lines);
    }

    /// <summary>Hosts that EasySSH can edit: <c>Host</c> blocks naming a single concrete alias.</summary>
    public IReadOnlyList<SshHostEntry> Hosts => ScanBlocks().Where(block => block.Entry is not null).Select(block => block.Entry!).ToArray();

    /// <summary>Finds an editable host.</summary>
    public SshHostEntry? Find(string alias) => FindBlock(alias)?.Entry;

    /// <summary>Whether a <c>Host</c> line of this file names <paramref name="alias"/>, alone or with other patterns.</summary>
    public bool Declares(string alias) =>
        ScanBlocks().Any(block => block.Patterns.Any(pattern => pattern.Equals(alias, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Adds a host, or rewrites the one named <paramref name="previousAlias"/> (or <c>host.Alias</c>) in place.
    /// </summary>
    /// <exception cref="ArgumentException">The alias is invalid, or another block already declares it.</exception>
    public void Upsert(SshHostEntry host, string? previousAlias = null)
    {
        if (!SshConfigText.IsValidAlias(host.Alias))
        {
            throw new ArgumentException($"\"{host.Alias}\" can't be a host alias.", nameof(host));
        }

        foreach (var option in host.Options)
        {
            if (!IsValidOption(option))
            {
                throw new ArgumentException($"Invalid option: {option}", nameof(host));
            }
        }

        var target = FindBlock(previousAlias ?? host.Alias);
        var renamed = target is not null && !target.Entry!.Alias.Equals(host.Alias, StringComparison.OrdinalIgnoreCase);
        if ((target is null || renamed) && Declares(host.Alias))
        {
            throw new ArgumentException($"\"{host.Alias}\" already exists.", nameof(host));
        }

        var rendered = Render(host);
        if (target is null)
        {
            if (_lines.Count > 0 && _lines[^1].Trim().Length > 0)
            {
                _lines.Add(string.Empty);
            }

            _lines.AddRange(rendered);
            return;
        }

        _lines.RemoveRange(target.Start, target.CoreEnd - target.Start);
        _lines.InsertRange(target.Start, rendered);
    }

    /// <summary>Removes a host. Returns false when this file has no editable host with that alias.</summary>
    public bool Remove(string alias)
    {
        var target = FindBlock(alias);
        if (target is null)
        {
            return false;
        }

        _lines.RemoveRange(target.Start, target.CoreEnd - target.Start);
        CollapseBlankLines();
        return true;
    }

    /// <summary>The file content.</summary>
    public string Serialize(string newLine = "\r\n") => string.Join(newLine, _lines) + newLine;

    /// <summary>Whether a host can be written: a valid alias and valid options.</summary>
    public static bool IsValidHost(SshHostEntry host) => SshConfigText.IsValidAlias(host.Alias) && host.Options.All(IsValidOption);

    /// <summary>Whether an option can be written in a host block.</summary>
    public static bool IsValidOption(SshOption option) =>
        option.Keyword.Length > 0
        && option.Keyword.All(char.IsAsciiLetterOrDigit)
        && !SshConfigText.SectionKeywords.Contains(option.Keyword)
        && option.Value.Trim().Length > 0
        && option.Value.IndexOfAny(['\r', '\n']) < 0;

    /// <summary>The lines of a host block.</summary>
    public static IReadOnlyList<string> Render(SshHostEntry host) =>
        ["Host " + host.Alias, .. host.Options.Select(option => Indent + option.Keyword + " " + option.Value.Trim())];

    private Block? FindBlock(string? alias) =>
        alias is null
            ? null
            : ScanBlocks().FirstOrDefault(block => block.Entry is not null && block.Entry.Alias.Equals(alias.Trim(), StringComparison.OrdinalIgnoreCase));

    private List<Block> ScanBlocks()
    {
        var blocks = new List<Block>();
        var starts = new List<int>();
        for (var index = 0; index < _lines.Count; index++)
        {
            if (SshConfigText.IsTrivia(_lines[index]))
            {
                continue;
            }

            var (keyword, _) = SshConfigText.SplitDirective(_lines[index]);
            if (keyword.Equals("Host", StringComparison.OrdinalIgnoreCase) || keyword.Equals("Match", StringComparison.OrdinalIgnoreCase))
            {
                starts.Add(index);
            }
        }

        for (var position = 0; position < starts.Count; position++)
        {
            var start = starts[position];
            var end = position + 1 < starts.Count ? starts[position + 1] : _lines.Count;

            // Comments and blank lines at the end of a block usually introduce the next one: keep them.
            var coreEnd = end;
            while (coreEnd > start + 1 && SshConfigText.IsTrivia(_lines[coreEnd - 1]))
            {
                coreEnd--;
            }

            var (keyword, value) = SshConfigText.SplitDirective(_lines[start]);
            var patterns = keyword.Equals("Host", StringComparison.OrdinalIgnoreCase) ? SshConfigText.Split(value) : [];
            SshHostEntry? entry = null;
            if (patterns is [var alias] && SshConfigText.IsValidAlias(alias))
            {
                var options = _lines.Skip(start + 1).Take(coreEnd - start - 1)
                    .Where(line => !SshConfigText.IsTrivia(line))
                    .Select(SshConfigText.SplitDirective)
                    .Select(pair => new SshOption(pair.Keyword, pair.Value));
                entry = new SshHostEntry(alias, options);
            }

            blocks.Add(new Block(start, coreEnd, patterns, entry));
        }

        return blocks;
    }

    private void CollapseBlankLines()
    {
        for (var index = _lines.Count - 1; index > 0; index--)
        {
            if (_lines[index].Trim().Length == 0 && _lines[index - 1].Trim().Length == 0)
            {
                _lines.RemoveAt(index);
            }
        }

        while (_lines.Count > 0 && _lines[^1].Trim().Length == 0)
        {
            _lines.RemoveAt(_lines.Count - 1);
        }

        while (_lines.Count > 0 && _lines[0].Trim().Length == 0)
        {
            _lines.RemoveAt(0);
        }
    }

    private sealed record Block(int Start, int CoreEnd, IReadOnlyList<string> Patterns, SshHostEntry? Entry);
}
