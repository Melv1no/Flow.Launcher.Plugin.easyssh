using Flow.Launcher.Plugin.EasySsh.Core;

namespace Flow.Launcher.Plugin.EasySsh.Tests.Core;

public class ExecutableResolverTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _tool;

    public ExecutableResolverTests()
    {
        _tool = _temp.Write(Path.Combine("bin", "tool.exe"), string.Empty);
    }

    private string BinFolder => Path.GetDirectoryName(_tool)!;

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("tool")]
    [InlineData("tool.exe")]
    [InlineData("TOOL.EXE")]
    [InlineData("\"tool.exe\"")]
    public void Finds_programs_in_path(string name)
    {
        var found = ExecutableResolver.Resolve(name, $@"C:\does-not-exist;""{BinFolder}""", ".COM;.EXE");

        Assert.Equal(_tool, found, ignoreCase: true);
    }

    [Fact]
    public void Returns_null_for_unknown_programs()
    {
        Assert.Null(ExecutableResolver.Resolve("missing", BinFolder, ".EXE"));
        Assert.Null(ExecutableResolver.Resolve("", BinFolder, ".EXE"));
        Assert.Null(ExecutableResolver.Resolve("tool", BinFolder, ".CMD"));
    }

    [Fact]
    public void Accepts_full_paths_with_or_without_extension()
    {
        Assert.Equal(_tool, ExecutableResolver.Resolve(_tool));
        Assert.Equal(_tool, ExecutableResolver.Resolve(Path.ChangeExtension(_tool, null), pathExtVariable: ".EXE"), ignoreCase: true);
    }

    [Fact]
    public void Expands_environment_variables()
    {
        Environment.SetEnvironmentVariable("EASYSSH_TEST_BIN", BinFolder);

        Assert.Equal(_tool, ExecutableResolver.Resolve(@"%EASYSSH_TEST_BIN%\tool.exe"));
    }

    [Fact]
    public void Does_not_search_relative_paths_in_path()
    {
        Assert.Null(ExecutableResolver.Resolve(@"bin\tool.exe", _temp.Path, ".EXE"));
    }

    [Fact]
    public void Finds_cmd_on_windows()
    {
        Assert.NotNull(ExecutableResolver.Resolve("cmd.exe"));
    }
}
