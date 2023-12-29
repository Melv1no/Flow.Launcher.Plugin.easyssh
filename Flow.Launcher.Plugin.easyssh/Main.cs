using System;
using System.Collections.Generic;
using System.Diagnostics;
using Flow.Launcher.Plugin;

namespace Flow.Launcher.Plugin.easyssh
{
    public class EasySSH : IPlugin
    {
        private PluginInitContext _context;

        public void Init(PluginInitContext context)
        {
            if (!Utils.IsSSHInstalled())
            {
                return;
            }
            _context = context;
        }

        public List<Result> Query(Query query)
        { 
            var result = new Result
            {
                Title = "EasySSH",
                SubTitle = $"connect to: {query.Search}",
                Action = c =>
                {        
                    string sshCommand = "ssh root@178.170.13.122";

                    new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "ssh.exe",
                            Arguments = query.Search,
                            RedirectStandardInput = false,
                            RedirectStandardOutput = false,
                            RedirectStandardError = false,
                            UseShellExecute = true,
                            CreateNoWindow = false
                        }
                    }.Start();
                    
                    return true;
                },
                IcoPath = "Images/app.png"
            };
            return new List<Result>() {result};
        }
    }
}