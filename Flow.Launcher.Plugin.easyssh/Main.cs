using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Flow.Launcher.Plugin;
using System.Linq;


namespace Flow.Launcher.Plugin.easyssh
{
    public class EasySSH : IPlugin
    {
        private PluginInitContext _context;
        private ProfileManager _profile;

        public void Init(PluginInitContext context)
        {
            if (!Utils.IsSSHInstalled())
            {
                return;
            }

            _context = context;
            _profile = new ProfileManager(_context.CurrentPluginMetadata.PluginDirectory + "\\profiles.json");
        }

        public List<Result> Query(Query query)
        {
            var easyssh_main_cmd = new Result();
            string subtitle = "<add,remove,profiles>/<direct ssh command>";
            String[] command = query.Search.Split(' ');

            switch (command[0])
            {
                case "add":
                    if (command.Length >= 3 && !string.IsNullOrEmpty(command[1]))
                    {
                        string sshCommand = string.Join(" ", command.Skip(2));
                        subtitle = sshCommand;
                        easyssh_main_cmd.Action = context =>
                        {
                            _profile.addProfile(command[1], sshCommand);
                            return true;
                        };
                    }
                    else
                    {
                        subtitle = "add <profile name> <ssh command>";
                    }

                    break;
                case "remove":
                    subtitle = "choose the profile you want remove";
                    List<Result> remove_result = new List<Result>();
                    remove_result.Add(new Result
                    {
                        Title = "Choose the profile to remove",
                        IcoPath = "app.png"
                    });
                    foreach (Profile profile in _profile.getProfiles())
                    {
                        remove_result.Add(new Result
                        {
                            Title = profile.Name,
                            SubTitle = profile.Command,
                            IcoPath = "app-red.png",
                            Action = context =>
                            {
                                _profile.removeProfile(profile.Id);
                                return true;
                            }
                        });
                    }
                    return remove_result;
                case "profiles":
                    subtitle = "profiles";
                    List<Result> profiles_result = new List<Result>();
                    foreach (Profile profile in _profile.getProfiles())
                    {
                        profiles_result.Add(new Result
                        {
                            Title = profile.Name,
                            SubTitle = profile.Command,
                            IcoPath = "app-green.png",
                            Action = context =>
                            {
                                new Process
                                {
                                    StartInfo = new ProcessStartInfo
                                    {
                                        FileName = "ssh.exe",
                                        Arguments = profile.Command,
                                        RedirectStandardInput = false,
                                        RedirectStandardOutput = false,
                                        RedirectStandardError = false,
                                        UseShellExecute = true,
                                        CreateNoWindow = false
                                    }
                                }.Start();
                                return true;
                            }
                        });
                    }

                    return profiles_result;
                case "d":
                    subtitle = $"direct connect to:{query.Search.Substring(1)}";
                    string commands = string.Join(" ", command.Skip(1));
                    easyssh_main_cmd.Action = context =>
                    {
                        new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "ssh.exe",
                                Arguments = commands,
                                RedirectStandardInput = false,
                                RedirectStandardOutput = false,
                                RedirectStandardError = false,
                                UseShellExecute = true,
                                CreateNoWindow = false
                            }
                        }.Start();

                        return true;
                    };
                    break;
                default:
                    subtitle = "usage: add ; remove ; profiles ; d";
                    break;
            }

            easyssh_main_cmd.Title = "EasySSH";
            easyssh_main_cmd.SubTitle = $"{subtitle}";
            easyssh_main_cmd.IcoPath = "app.png";
            return new List<Result>() { easyssh_main_cmd };
        }
    }
}