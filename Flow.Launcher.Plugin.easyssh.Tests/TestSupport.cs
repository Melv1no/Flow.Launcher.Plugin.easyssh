using System.Runtime.ExceptionServices;
using System.Xml.Linq;
using Flow.Launcher.Plugin.EasySsh.Storage;
using NSubstitute;

namespace Flow.Launcher.Plugin.EasySsh.Tests;

/// <summary>A temporary folder deleted at the end of the test.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "easyssh-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    /// <summary>Writes a file (creating its folder) and returns its full path.</summary>
    public string Write(string relativePath, string content)
    {
        var fullPath = Combine(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A file may still be locked by a failed test; it's only a temp folder.
        }
    }
}

/// <summary>Locations in the repository.</summary>
internal static class Repository
{
    public static string Root { get; } = FindRoot();

    public static string PluginProject => Path.Combine(Root, "Flow.Launcher.Plugin.easyssh");

    public static string LanguageFile(string language) => Path.Combine(PluginProject, "Languages", language + ".xaml");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Flow.Launcher.Plugin.easyssh", "plugin.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }
}

/// <summary>Translations read from the language files, so tests see the real English texts.</summary>
internal static class Translations
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static IReadOnlyDictionary<string, string> English { get; } = Load("en");

    public static IReadOnlyDictionary<string, string> Load(string language) =>
        XDocument.Load(Repository.LanguageFile(language))
            .Root!
            .Elements()
            .ToDictionary(element => (string)element.Attribute(XamlNamespace + "Key")!, element => element.Value);
}

/// <summary>Builds a substitute of Flow Launcher's API.</summary>
internal static class FakeApi
{
    public static IPublicAPI Create()
    {
        var api = Substitute.For<IPublicAPI>();
        api.GetTranslation(Arg.Any<string>()).Returns(call =>
        {
            var key = call.Arg<string>();
            return Translations.English.TryGetValue(key, out var text) ? text : $"No Translation for key {key}";
        });
        return api;
    }
}

/// <summary>A plugin initialized on temporary folders.</summary>
internal sealed class PluginFixture : IDisposable
{
    public PluginFixture(Action<TempDirectory>? arrange = null)
    {
        arrange?.Invoke(Temp);
        Plugin.Initialize(Api, DataDirectory, LegacyPath, SshConfigPath);
        if (Plugin.Services is { } services)
        {
            // Results must not depend on the machine running the tests.
            services.OpenSshInstalled = true;
        }
    }

    public TempDirectory Temp { get; } = new();

    public IPublicAPI Api { get; } = FakeApi.Create();

    public Main Plugin { get; } = new();

    public string DataDirectory => Temp.Combine("data");

    public string LegacyPath => Temp.Combine("ssh", "profiles.json");

    public string SshConfigPath => Temp.Combine("ssh", "config");

    public PluginServices Services => Plugin.Services ?? throw new InvalidOperationException("Plugin not initialized", Plugin.InitError);

    public PluginData Data => Services.Store.Data;

    /// <summary>Runs a query the way Flow Launcher does, results sorted by score.</summary>
    public List<Result> Query(string search, string actionKeyword = "ssh") =>
        Plugin.Answer(search, actionKeyword).OrderByDescending(result => result.Score).ToList();

    public void Dispose() => Temp.Dispose();
}

internal static class Sta
{
    /// <summary>Runs <paramref name="action"/> on an STA thread, as WPF requires.</summary>
    public static void Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                error = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}

internal static class ResultExtensions
{
    /// <summary>Invokes the action of a result like Flow Launcher does on Enter.</summary>
    public static bool Run(this Result result) =>
        result.Action?.Invoke(new ActionContext()) ?? throw new InvalidOperationException($"\"{result.Title}\" has no action");
}
