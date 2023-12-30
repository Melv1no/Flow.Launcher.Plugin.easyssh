using System.Diagnostics;

namespace Flow.Launcher.Plugin.easyssh
{
    /// <summary>
    /// Utility class for handling SSH-related operations.
    /// </summary>
    public abstract class Utils
    {
        /// <summary>
        /// Checks if SSH is installed on the system.
        /// </summary>
        /// <returns><c>true</c> if SSH is installed; otherwise, <c>false</c>.</returns>
        public static bool IsSshInstalled()
        {
            // Create a new process to run the 'where' command to locate the 'ssh' executable.
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
}