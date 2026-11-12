using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Flow.Launcher.Plugin.EasySsh.Core;

/// <summary>
/// Asks the installed OpenSSH client whether it accepts a configuration, before it is written: ssh stops on an
/// unknown keyword or a malformed line, for every host and every program using the configuration.
/// </summary>
public static class SshConfigValidator
{
    private const int TimeoutMilliseconds = 10_000;

    /// <summary>
    /// Runs <c>ssh -F &lt;file&gt; -G</c> on <paramref name="text"/>: ssh reads the whole file and prints the resulting
    /// options, without connecting nor reading any key.
    /// </summary>
    /// <param name="text">Content of an ssh_config file.</param>
    /// <param name="displayName">Name of the file in the messages, instead of the temporary file.</param>
    /// <returns>Null when ssh accepts it (or can't be run: nothing to break then), otherwise ssh's message.</returns>
    public static string? Validate(string text, string displayName)
    {
        var windowsSsh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe");
        var ssh = ExecutableResolver.Resolve("ssh.exe") ?? (File.Exists(windowsSsh) ? windowsSsh : null);
        if (ssh is null)
        {
            return null;
        }

        var path = Path.Combine(Path.GetTempPath(), "easyssh-check-" + Guid.NewGuid().ToString("N") + ".config");
        try
        {
            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var startInfo = new ProcessStartInfo(ssh)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("-F");
            startInfo.ArgumentList.Add(path);
            startInfo.ArgumentList.Add("-G");
            startInfo.ArgumentList.Add("easyssh-check");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            // Both streams are read: ssh -G prints many lines, and a full pipe would block it.
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeoutMilliseconds))
            {
                process.Kill();
                return null;
            }

            return process.ExitCode == 0 ? null : Describe(error.Result, path, displayName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Only a temporary file.
            }
        }
    }

    /// <summary>The lines of ssh's message that explain the problem, with the real file name.</summary>
    private static string Describe(string error, string path, string displayName)
    {
        // However ssh writes the path (slashes, drive prefix), it ends with the temporary file name.
        var temporaryFile = new System.Text.RegularExpressions.Regex(
            @"\S*" + System.Text.RegularExpressions.Regex.Escape(Path.GetFileName(path)),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var lines = error
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => temporaryFile.Replace(line, displayName))
            .Where(line => !line.Contains("terminating", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();
        return lines.Count > 0 ? string.Join(" ", lines) : temporaryFile.Replace(error.Trim(), displayName);
    }
}
