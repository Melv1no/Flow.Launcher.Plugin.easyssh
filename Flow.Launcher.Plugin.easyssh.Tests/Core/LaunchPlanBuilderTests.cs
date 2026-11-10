using System.Text;
using Flow.Launcher.Plugin.EasySsh.Core;

namespace Flow.Launcher.Plugin.EasySsh.Tests.Core;

public class LaunchPlanBuilderTests
{
    private const string Pause = "Press Enter to close";

    private static LaunchPlan Build(TerminalDefinition terminal, string command, string title = "prod", bool keepOpen = true) =>
        LaunchPlanBuilder.Build(terminal, command, title, keepOpen, Pause);

    [Fact]
    public void Command_prompt_runs_the_command_and_waits_on_ssh_errors()
    {
        var plan = Build(BuiltInTerminals.CommandPrompt, "ssh root@host");

        Assert.Equal("cmd.exe", plan.FileName);
        Assert.Equal("/s /c \"title prod & ssh root@host & if errorlevel 255 pause\"", plan.Arguments);
    }

    [Fact]
    public void Command_prompt_can_close_immediately()
    {
        var plan = Build(BuiltInTerminals.CommandPrompt, "ssh root@host", keepOpen: false);

        Assert.Equal("/s /c \"title prod & ssh root@host\"", plan.Arguments);
    }

    [Fact]
    public void Command_prompt_keeps_inner_quotes()
    {
        var plan = Build(BuiltInTerminals.CommandPrompt, "ssh -i \"C:\\my keys\\id\" host", keepOpen: false);

        Assert.Equal("/s /c \"title prod & ssh -i \"C:\\my keys\\id\" host\"", plan.Arguments);
    }

    [Fact]
    public void Command_prompt_escapes_the_title()
    {
        var plan = Build(BuiltInTerminals.CommandPrompt, "ssh x", "R&D \"lab\" (100%)", keepOpen: false);

        Assert.Equal("/s /c \"title R^&D lab ^(100^) & ssh x\"", plan.Arguments);
    }

    [Fact]
    public void Windows_terminal_opens_a_tab_in_the_last_window()
    {
        var plan = Build(BuiltInTerminals.WindowsTerminal, "ssh -p 22 root@host", "My Server");

        Assert.Equal("wt.exe", plan.FileName);
        Assert.Equal("-w 0 new-tab --title \"My Server\" ssh -p 22 root@host", plan.Arguments);
    }

    [Theory]
    [InlineData("ssh host ls ;", "ssh host ls \\;")]
    [InlineData("ssh host ls; pwd", "ssh host ls\\; pwd")]
    [InlineData("ssh host \"a;b\"", "ssh host \"a;b\"")]
    public void Windows_terminal_escapes_its_command_separator(string command, string expected)
    {
        var plan = Build(BuiltInTerminals.WindowsTerminal, command, "t");

        Assert.Equal("-w 0 new-tab --title t " + expected, plan.Arguments);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PowerShell_receives_an_encoded_script(bool keepOpen)
    {
        var plan = Build(BuiltInTerminals.PowerShell, "ssh -i \"C:\\k\" host", "it's prod", keepOpen);

        Assert.Equal("pwsh.exe", plan.FileName);
        Assert.StartsWith("-NoLogo -EncodedCommand ", plan.Arguments);
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(plan.Arguments["-NoLogo -EncodedCommand ".Length..]));
        Assert.Contains("$Host.UI.RawUI.WindowTitle = 'it''s prod'\n", script);
        Assert.Contains("\nssh -i \"C:\\k\" host\n", script);
        Assert.Equal(keepOpen, script.Contains("if ($LASTEXITCODE -eq 255) { Read-Host 'Press Enter to close' | Out-Null }"));
    }

    [Fact]
    public void Windows_PowerShell_uses_powershell_exe()
    {
        Assert.Equal("powershell.exe", Build(BuiltInTerminals.WindowsPowerShell, "ssh host").FileName);
    }

    [Theory]
    [InlineData("-p \"Git Bash\" {command}", "-p \"Git Bash\" ssh root@host")]
    [InlineData("-p \"Git Bash\"", "-p \"Git Bash\" ssh root@host")]
    [InlineData("", "ssh root@host")]
    [InlineData("--title {name} -- {COMMAND}", "--title prod -- ssh root@host")]
    [InlineData("-e \"{command}\"", "-e \"ssh root@host\"")]
    public void Custom_terminals_expand_their_template(string template, string expected)
    {
        var plan = Build(new TerminalDefinition("custom", "kitty.exe", template), "ssh root@host");

        Assert.Equal(new LaunchPlan("kitty.exe", expected), plan);
    }

    [Fact]
    public void Placeholders_inside_the_command_are_not_replaced()
    {
        var plan = Build(new TerminalDefinition("custom", "x.exe", "{command}"), "ssh host echo {name}");

        Assert.Equal("ssh host echo {name}", plan.Arguments);
    }

    [Fact]
    public void Built_in_names_are_reserved()
    {
        Assert.True(BuiltInTerminals.IsReservedName("CMD"));
        Assert.True(BuiltInTerminals.IsReservedName("wt"));
        Assert.False(BuiltInTerminals.IsReservedName("wt.exe"));
        Assert.Same(BuiltInTerminals.PowerShell, BuiltInTerminals.Find("PWSH"));
    }
}
