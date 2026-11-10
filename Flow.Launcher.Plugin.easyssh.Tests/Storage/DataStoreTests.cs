using System.Text.Json;
using Flow.Launcher.Plugin.EasySsh.Storage;

namespace Flow.Launcher.Plugin.EasySsh.Tests.Storage;

public class DataStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    private string FilePath => Path.Combine(_temp.Path, DataStore.FileName);

    public void Dispose() => _temp.Dispose();

    private static PluginData WithProfile(PluginData data, string name) =>
        data.UpsertProfile(new SshProfile(name, "ssh " + name));

    [Fact]
    public void Opening_writes_nothing_until_the_first_change()
    {
        var folder = _temp.Combine("not-created-yet");

        var store = DataStore.Open(folder);

        Assert.Equal(DataLoadStatus.Created, store.LoadStatus);
        Assert.Empty(store.Data.Profiles);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Changes_are_saved_and_read_back()
    {
        var store = DataStore.Open(_temp.Path);
        store.Update(data => WithProfile(data, "prod") with { KeepWindowOpenOnError = false });

        var reopened = DataStore.Open(_temp.Path);

        Assert.Equal(DataLoadStatus.Loaded, reopened.LoadStatus);
        Assert.Equal(new SshProfile("prod", "ssh prod"), Assert.Single(reopened.Data.Profiles));
        Assert.False(reopened.Data.KeepWindowOpenOnError);
    }

    [Fact]
    public void The_file_is_readable_json()
    {
        DataStore.Open(_temp.Path).Update(data => WithProfile(data, "prod"));

        using var json = JsonDocument.Parse(File.ReadAllText(FilePath));
        Assert.Equal(PluginData.CurrentVersion, json.RootElement.GetProperty("Version").GetInt32());
        Assert.Equal("ssh prod", json.RootElement.GetProperty("Profiles")[0].GetProperty("Command").GetString());
        Assert.Contains("\n", File.ReadAllText(FilePath));
    }

    [Fact]
    public void The_previous_version_is_kept_as_backup()
    {
        var store = DataStore.Open(_temp.Path);
        store.Update(data => WithProfile(data, "first"));
        store.Update(data => WithProfile(data, "second"));

        var backup = File.ReadAllText(FilePath + ".bak");

        Assert.Contains("first", backup);
        Assert.DoesNotContain("second", backup);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void A_damaged_file_is_restored_from_the_backup_and_kept_aside()
    {
        var store = DataStore.Open(_temp.Path);
        store.Update(data => WithProfile(data, "first"));
        store.Update(data => WithProfile(data, "second"));
        File.WriteAllText(FilePath, "{ this is not json");

        var reopened = DataStore.Open(_temp.Path);

        Assert.Equal(DataLoadStatus.RestoredFromBackup, reopened.LoadStatus);
        Assert.Equal(["first"], reopened.Data.Profiles.Select(profile => profile.Name));
        Assert.NotNull(reopened.DamagedFileCopy);
        Assert.Equal("{ this is not json", File.ReadAllText(reopened.DamagedFileCopy!));
        Assert.Equal(DataLoadStatus.Loaded, DataStore.Open(_temp.Path).LoadStatus);
    }

    [Fact]
    public void A_damaged_file_without_backup_is_kept_and_never_overwritten()
    {
        File.WriteAllText(FilePath, "[1, 2, 3]");

        var store = DataStore.Open(_temp.Path);

        Assert.Equal(DataLoadStatus.ResetAfterCorruption, store.LoadStatus);
        Assert.Empty(store.Data.Profiles);
        Assert.Equal("[1, 2, 3]", File.ReadAllText(store.DamagedFileCopy!));
    }

    [Fact]
    public void A_missing_file_is_restored_from_the_backup()
    {
        var store = DataStore.Open(_temp.Path);
        store.Update(data => WithProfile(data, "first"));
        store.Update(data => WithProfile(data, "second"));
        File.Delete(FilePath);

        var reopened = DataStore.Open(_temp.Path);

        Assert.Equal(DataLoadStatus.RestoredFromBackup, reopened.LoadStatus);
        Assert.Single(reopened.Data.Profiles);
        Assert.True(File.Exists(FilePath));
    }

    [Fact]
    public void A_locked_file_fails_without_writing_anything()
    {
        DataStore.Open(_temp.Path).Update(data => WithProfile(data, "precious"));
        var before = File.ReadAllText(FilePath);

        using (new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => DataStore.Open(_temp.Path));
        }

        Assert.Equal(before, File.ReadAllText(FilePath));
    }

    [Fact]
    public void Properties_of_newer_versions_are_preserved()
    {
        File.WriteAllText(FilePath, """{ "Version": 4, "Profiles": [], "FutureSetting": { "x": 1 } }""");

        DataStore.Open(_temp.Path).Update(data => WithProfile(data, "prod"));

        using var json = JsonDocument.Parse(File.ReadAllText(FilePath));
        Assert.Equal(1, json.RootElement.GetProperty("FutureSetting").GetProperty("x").GetInt32());
    }

    [Fact]
    public void A_failing_change_changes_nothing()
    {
        var store = DataStore.Open(_temp.Path);
        store.Update(data => WithProfile(data, "prod"));
        var before = store.Data;

        Assert.Throws<ArgumentException>(() => store.Update(data => data.UpsertProfile(new SshProfile("", "x"))));

        Assert.Same(before, store.Data);
        Assert.Single(DataStore.Open(_temp.Path).Data.Profiles);
    }

    [Fact]
    public void Changes_raise_an_event_and_identical_data_does_not()
    {
        var store = DataStore.Open(_temp.Path);
        var raised = 0;
        store.Changed += (_, _) => raised++;

        store.Update(data => WithProfile(data, "prod"));
        store.Update(data => data);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Concurrent_changes_are_all_kept()
    {
        var store = DataStore.Open(_temp.Path);

        Parallel.For(0, 40, index => store.Update(data => WithProfile(data, "host" + index)));

        Assert.Equal(40, DataStore.Open(_temp.Path).Data.Profiles.Count);
    }

    [Fact]
    public void Reload_reads_changes_made_by_hand()
    {
        var store = DataStore.Open(_temp.Path);
        store.Update(data => WithProfile(data, "prod"));
        File.WriteAllText(FilePath, """{ "Profiles": [ { "Name": "edited", "Command": "ssh edited" } ] }""");

        store.Reload();

        Assert.Equal("edited", Assert.Single(store.Data.Profiles).Name);
    }
}
