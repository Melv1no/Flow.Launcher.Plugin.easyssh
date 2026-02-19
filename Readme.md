Flow.Launcher.Plugin.easyssh
==================

A plugin for the [Flow launcher](https://github.com/Flow-Launcher/Flow.Launcher).

Currently, this plugin enables you to establish an SSH connection using a single command line. In the future, I plan to integrate a NoSQL database to store various SSH connection profiles.
![image](https://github.com/Melv1no/Flow.Launcher.Plugin.easyssh/assets/66535418/2b864355-51c6-4a9a-b4ad-9460cf9328d3)

### Installation

    pm install EasySSH or download last release and unzip in %appdata%/FlowLauncher/plugins

### Usage

    ssh d <direct ssh args | root@127.0.0.1>
    ssh profiles (select profile to connect)
    ssh remove (select profile to delete)
    ssh add <profile name | TestProfile> <ssh args | root@127.0.0.1>

### Custom Shell

By default, SSH connections are opened with `cmd.exe`. You can configure a custom shell:

    ssh shell add <exe>

Example:

    ssh shell add wt.exe

You can also define a named shell with custom arguments. This is useful when the executable needs extra parameters (e.g. selecting a Windows Terminal profile):

    ssh shell add <name> <exe + args>

Example:

    ssh shell add wt-gitbash wt.exe -p "Git Bash"

When a profile SSH command runs, the plugin will execute:

    wt.exe -p "Git Bash" ssh user@host

Other shell commands:

    ssh shell              (list configured shells)
    ssh shell remove       (select a shell to delete)

### To do

    ssh scp d <file> <user@host>:<destination>
    ssh scp profiles <file> <destination>
