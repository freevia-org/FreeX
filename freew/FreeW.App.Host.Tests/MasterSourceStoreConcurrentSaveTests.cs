using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

// Covers R154 finding shared-options-persistence F2: MasterSourceStore.Load()/Save() had no
// reload-before-save merge, so a second window/process saving master-sources.json could silently
// erase a citation source another window had already persisted. See
// MasterSourceStore.Save(baseline, edited) / MergeOntoFreshLoad.
public sealed class MasterSourceStoreConcurrentSaveTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeW.MasterSourceStoreConcurrentSaveTests-");

    public void Dispose() => _temporaryDirectory.Dispose();

    private string TemporaryPath(string fileName) => Path.Combine(_temporaryDirectory.Path, fileName);

    // Exercises the exact pipeline MasterSourceStore.Save(baseline, edited) runs internally
    // (reload fresh from disk, then MergeOntoFreshLoad, then write) against a temp-redirected
    // JsonSettingsStore<MasterSourceStore>, so the test never touches the real product data
    // directory the static Store() singleton is bound to.
    private static void SaveWithMerge(
        JsonSettingsStore<MasterSourceStore> settingsStore,
        MasterSourceStore baseline,
        MasterSourceStore edited)
    {
        var fresh = settingsStore.Load();
        MasterSourceStore.MergeOntoFreshLoad(fresh, baseline, edited);
        settingsStore.Save(fresh);
    }

    [Fact]
    public void Save_TwoWindowsAddDifferentSourcesFromSameBaseline_BothSourcesSurvive()
    {
        var path = TemporaryPath("master-sources-concurrent-add.json");
        var settingsStore = JsonSettingsStore<MasterSourceStore>.ForPath(path);

        // Window A and window B both Load() the same (empty) file before either saves --
        // exactly the "second window opened before A's save" gesture from the finding.
        var baselineA = settingsStore.Load();
        var baselineB = settingsStore.Load();

        var editedA = new MasterSourceStore();
        editedA.AddOrUpdate(new Source { Tag = "Alpha2020", Author = "Author A", Title = "Title A", Year = "2020" });
        SaveWithMerge(settingsStore, baselineA, editedA);

        // Window B still holds its now-stale baseline (from before A's save) and adds a
        // different source, then saves. Pre-fix, this Save() would silently discard Alpha2020.
        var editedB = new MasterSourceStore();
        editedB.AddOrUpdate(new Source { Tag = "Bravo2021", Author = "Author B", Title = "Title B", Year = "2021" });
        SaveWithMerge(settingsStore, baselineB, editedB);

        var onDisk = settingsStore.Load();
        onDisk.Sources.Select(s => s.Tag).Should().BeEquivalentTo(new[] { "Alpha2020", "Bravo2021" });
    }

    [Fact]
    public void Save_SecondWindowEditsSourceItAddedItself_UnrelatedConcurrentSourceIsUnaffected()
    {
        // Sibling / no-regression case: window B's own edit to a tag it already owns must still
        // apply (this is not a case of "another window's data" -- it's B updating its own prior
        // save), and must not disturb the source window A independently persisted meanwhile.
        var path = TemporaryPath("master-sources-concurrent-edit.json");
        var settingsStore = JsonSettingsStore<MasterSourceStore>.ForPath(path);

        var baselineB = settingsStore.Load();
        var editedBFirst = new MasterSourceStore();
        editedBFirst.AddOrUpdate(new Source { Tag = "Bravo2021", Author = "Author B", Title = "Title B", Year = "2021" });
        SaveWithMerge(settingsStore, baselineB, editedBFirst);

        // Window A now loads (sees Bravo2021) and adds its own source.
        var baselineA = settingsStore.Load();
        var editedA = new MasterSourceStore();
        editedA.AddOrUpdate(new Source { Tag = "Alpha2020", Author = "Author A", Title = "Title A", Year = "2020" });
        SaveWithMerge(settingsStore, baselineA, editedA);

        // Window B (still on its earlier baseline, before A's save) edits the title of the
        // source it itself added and saves again.
        var editedBSecond = new MasterSourceStore();
        editedBSecond.AddOrUpdate(new Source { Tag = "Bravo2021", Author = "Author B", Title = "Title B Revised", Year = "2021" });
        SaveWithMerge(settingsStore, baselineB, editedBSecond);

        var onDisk = settingsStore.Load();
        onDisk.Sources.Select(s => s.Tag).Should().BeEquivalentTo(new[] { "Alpha2020", "Bravo2021" });
        onDisk.Sources.Single(s => s.Tag == "Bravo2021").Title.Should().Be("Title B Revised");
        onDisk.Sources.Single(s => s.Tag == "Alpha2020").Title.Should().Be("Title A");
    }
}
