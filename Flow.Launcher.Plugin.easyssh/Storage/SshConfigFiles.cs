using System.Text;
using Flow.Launcher.Plugin.EasySsh.Core;

namespace Flow.Launcher.Plugin.EasySsh.Storage;

/// <summary>The installed ssh refused a configuration: nothing was written.</summary>
public sealed class SshConfigRejectedException(string sshMessage) : ArgumentException(sshMessage)
{
    /// <summary>What ssh said.</summary>
    public string SshMessage { get; } = sshMessage;
}

/// <summary>
/// The ssh_config files EasySSH works with: the user's <c>~/.ssh/config</c>, which it only reads except for one
/// <c>Include</c> line, and <c>~/.ssh/easyssh.config</c>, where it writes the hosts saved with it.
/// </summary>
public sealed class SshConfigFiles
{
    /// <summary>Name of the file where EasySSH writes its hosts.</summary>
    public const string ManagedFileName = "easyssh.config";

    private static readonly byte[] Utf8ByteOrderMark = [0xEF, 0xBB, 0xBF];

    /// <summary>Creates the set of files around <paramref name="mainConfigPath"/>.</summary>
    /// <param name="mainConfigPath">The user configuration, usually <c>~/.ssh/config</c>. It may not exist.</param>
    public SshConfigFiles(string mainConfigPath)
    {
        MainConfigPath = Path.GetFullPath(mainConfigPath);
        SshDirectory = Path.GetDirectoryName(MainConfigPath) ?? string.Empty;
        ManagedPath = Path.Combine(SshDirectory, ManagedFileName);
    }

    /// <summary>The user configuration (<c>~/.ssh/config</c>).</summary>
    public string MainConfigPath { get; }

    /// <summary>The folder of the configuration (<c>~/.ssh</c>), used for relative <c>Include</c> paths.</summary>
    public string SshDirectory { get; }

    /// <summary>The file written by EasySSH (<c>~/.ssh/easyssh.config</c>).</summary>
    public string ManagedPath { get; }

    /// <summary>The line that includes the managed file.</summary>
    public static string IncludeLine => "Include " + ManagedFileName;

    /// <summary>
    /// Checks a configuration before it is written: returns null when it is accepted, otherwise why not.
    /// By default, the installed ssh is asked (<see cref="SshConfigValidator"/>).
    /// </summary>
    internal Func<string, string?> Validator { get; set; } = static text => SshConfigValidator.Validate(text, ManagedFileName);

    /// <summary>Whether <paramref name="path"/> is the managed file.</summary>
    public bool IsManaged(string? path) =>
        path is not null && string.Equals(Path.GetFullPath(path), ManagedPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads the managed file, or returns an empty one when it doesn't exist yet.</summary>
    /// <exception cref="IOException">The file exists but can't be read.</exception>
    public ManagedConfig ReadManaged() =>
        File.Exists(ManagedPath) ? ManagedConfig.Parse(File.ReadAllText(ManagedPath)) : ManagedConfig.CreateEmpty();

    /// <summary>Whether ssh accepts <paramref name="host"/>: null when it does, otherwise why not.</summary>
    public string? Validate(SshHostEntry host)
    {
        var config = ManagedConfig.CreateEmpty();
        config.Upsert(host);
        return Validator(config.Serialize());
    }

    /// <summary>
    /// Saves the managed file (the previous version is kept as <c>easyssh.config.bak</c>), then makes sure
    /// <c>~/.ssh/config</c> includes it. Nothing is written when ssh refuses the new content.
    /// </summary>
    /// <returns>Whether the <c>Include</c> line had to be added.</returns>
    /// <exception cref="SshConfigRejectedException">ssh refuses the new content.</exception>
    /// <exception cref="IOException">A file can't be written.</exception>
    /// <exception cref="UnauthorizedAccessException">A file can't be written.</exception>
    public bool Save(ManagedConfig config)
    {
        var text = config.Serialize();
        if (Validator(text) is { } error)
        {
            throw new SshConfigRejectedException(error);
        }

        // A ~/.ssh/config that can't receive the Include line is reported before anything is written.
        _ = File.Exists(MainConfigPath) ? ReadForScan(File.ReadAllBytes(MainConfigPath), out _) : string.Empty;

        AtomicFile.WriteText(ManagedPath, text, ManagedPath + ".bak");
        return EnsureIncluded();
    }

    /// <summary>Whether <c>~/.ssh/config</c> includes the managed file before its first <c>Host</c> or <c>Match</c>.</summary>
    public bool IsIncluded() => File.Exists(MainConfigPath) && IncludesManagedFile(ReadForScan(File.ReadAllBytes(MainConfigPath), out _));

    /// <summary>
    /// Adds the <c>Include</c> line at the top of <c>~/.ssh/config</c> (creating the file if needed) when it is
    /// missing. The original bytes are kept as they are after the inserted lines: whatever the encoding, the line
    /// endings and the permissions, nothing else changes. The previous version is saved as <c>config.easyssh.bak</c>.
    /// </summary>
    /// <returns>Whether the line was added.</returns>
    /// <exception cref="IOException">The file can't be written, or is in UTF-16 (which ssh can't read).</exception>
    public bool EnsureIncluded()
    {
        var exists = File.Exists(MainConfigPath);
        var original = exists ? File.ReadAllBytes(MainConfigPath) : [];
        var text = ReadForScan(original, out var byteOrderMarkLength);
        if (IncludesManagedFile(text))
        {
            return false;
        }

        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\n') ? "\n" : "\r\n";
        var block = new StringBuilder()
            .Append("# Added by EasySSH (Flow Launcher): the hosts saved with EasySSH are in ").Append(ManagedFileName).Append('.').Append(newLine)
            .Append("# Keep this line before the first Host or Match block.").Append(newLine)
            .Append(IncludeLine).Append(newLine);
        if (text.Length > 0)
        {
            block.Append(newLine);
        }

        byte[] content = [.. original.AsSpan(0, byteOrderMarkLength), .. Encoding.ASCII.GetBytes(block.ToString()), .. original.AsSpan(byteOrderMarkLength)];
        AtomicFile.Write(MainConfigPath, content, exists ? MainConfigPath + ".easyssh.bak" : null);
        return true;
    }

    /// <summary>
    /// The text of a configuration, only to look for keywords: Latin-1 maps every byte to one character, so
    /// nothing is lost whatever the real encoding. UTF-16 is refused: ssh can't read it.
    /// </summary>
    private static string ReadForScan(byte[] bytes, out int byteOrderMarkLength)
    {
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
        {
            throw new IOException("~/.ssh/config is saved in UTF-16, which ssh can't read: save it as UTF-8, then try again.");
        }

        byteOrderMarkLength = bytes.AsSpan().StartsWith(Utf8ByteOrderMark) ? Utf8ByteOrderMark.Length : 0;
        return Encoding.Latin1.GetString(bytes, byteOrderMarkLength, bytes.Length - byteOrderMarkLength);
    }

    private bool IncludesManagedFile(string text)
    {
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (SshConfigText.IsTrivia(line))
            {
                continue;
            }

            var (keyword, value) = SshConfigText.SplitDirective(line);
            if (keyword.Equals("Host", StringComparison.OrdinalIgnoreCase) || keyword.Equals("Match", StringComparison.OrdinalIgnoreCase))
            {
                // An Include inside a block only applies to that block.
                return false;
            }

            // Save() writes the managed file first, so a wildcard such as "Include *.config" already lists it.
            if (keyword.Equals("Include", StringComparison.OrdinalIgnoreCase)
                && SshConfigText.Split(value).Any(pattern => SshConfigParser.ResolveInclude(pattern, SshDirectory).Any(IsManaged)))
            {
                return true;
            }
        }

        return false;
    }
}
