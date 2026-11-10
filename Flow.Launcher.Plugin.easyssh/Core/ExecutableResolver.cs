namespace Flow.Launcher.Plugin.EasySsh.Core;

/// <summary>
/// Finds executables like <c>where.exe</c> does (PATH + PATHEXT), without starting a process.
/// </summary>
public static class ExecutableResolver
{
    private static readonly string[] DefaultExtensions = [".COM", ".EXE", ".BAT", ".CMD"];

    /// <summary>
    /// Returns the full path of <paramref name="executable"/>, or null when it can't be found.
    /// Environment variables are expanded and surrounding quotes removed.
    /// </summary>
    /// <param name="executable">A program name (<c>wt.exe</c>, <c>pwsh</c>) or a path.</param>
    /// <param name="pathVariable">Value to use instead of the PATH environment variable.</param>
    /// <param name="pathExtVariable">Value to use instead of the PATHEXT environment variable.</param>
    public static string? Resolve(string? executable, string? pathVariable = null, string? pathExtVariable = null)
    {
        var name = Environment.ExpandEnvironmentVariables(executable ?? string.Empty).Trim().Trim('"');
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return null;
        }

        string[] extensions;
        if (Path.HasExtension(name))
        {
            extensions = [string.Empty];
        }
        else
        {
            var fromEnvironment = (pathExtVariable ?? Environment.GetEnvironmentVariable("PATHEXT"))?
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            extensions = fromEnvironment is { Length: > 0 } ? fromEnvironment : DefaultExtensions;
        }

        if (Path.IsPathRooted(name))
        {
            return FindWithExtensions(name, extensions);
        }

        if (name.IndexOfAny(['\\', '/']) >= 0)
        {
            // A relative path with folders is not looked up in PATH (same as cmd.exe).
            return null;
        }

        var directories = pathVariable is null ? SearchDirectories() : SplitPath(pathVariable);
        foreach (var directory in directories)
        {
            string candidate;
            try
            {
                candidate = Path.Combine(Environment.ExpandEnvironmentVariables(directory.Trim('"')), name);
            }
            catch (ArgumentException)
            {
                continue;
            }

            var found = FindWithExtensions(candidate, extensions);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Whether the OpenSSH client (ssh.exe) is available.</summary>
    public static bool IsOpenSshInstalled() =>
        Resolve("ssh.exe") is not null
        || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe"));

    /// <summary>
    /// Folders searched by default: the PATH of this process, then the machine and user PATH as currently saved
    /// in Windows (Flow Launcher runs for days: a program installed meanwhile is missing from its own PATH),
    /// then the folder of app execution aliases (<c>wt.exe</c>, <c>winget.exe</c>…), sometimes missing from PATH.
    /// </summary>
    private static IEnumerable<string> SearchDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new[]
        {
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps"),
        };
        foreach (var source in sources)
        {
            foreach (var directory in SplitPath(source))
            {
                if (seen.Add(directory))
                {
                    yield return directory;
                }
            }
        }
    }

    private static string[] SplitPath(string? path) =>
        (path ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? FindWithExtensions(string pathWithoutExtension, IEnumerable<string> extensions)
    {
        foreach (var extension in extensions)
        {
            var candidate = pathWithoutExtension + extension;
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
