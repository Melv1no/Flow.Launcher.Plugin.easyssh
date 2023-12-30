using System.Diagnostics;

namespace Flow.Launcher.Plugin.easyssh;
public class Utils
{
    public static bool IsSSHInstalled()
    {
        Process process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "ssh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return !string.IsNullOrEmpty(output) && string.IsNullOrEmpty(error);
    }
    
    
}