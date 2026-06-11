using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class AutosaveServiceTests
{
    private sealed class StubSource : IAutosaveWorkbookSource
    {
        public StubSource(bool dirty = false, int generation = 0, string? filePath = null, string? name = null)
        {
            var wb = new Workbook(name ?? "Test");
            Workbook = wb;
            IsWorkbookDirty = dirty;
            WorkbookDirtyGeneration = generation;
            CurrentFilePath = filePath;
            DisplayName = name ?? "Test";
        }

        public Workbook Workbook { get; }
        public string? CurrentFilePath { get; }
        public string DisplayName { get; }
        public bool IsWorkbookDirty { get; set; }
        public int WorkbookDirtyGeneration { get; set; }
    }

    [Fact]
    public void OnTimerTick_WhenDirtyAndGenerationChanged_WritesSnapshot()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);
        var service = new AutosaveService(store);
        var source = new StubSource(dirty: true, generation: 1);

        service.Attach(source, "test-w0");
        service.OnTimerTick();

        File.Exists(store.GetSnapshotPath("test-w0")).Should().BeTrue();
        File.Exists(store.GetSidecarPath("test-w0")).Should().BeTrue();
    }

    [Fact]
    public void OnTimerTick_WhenNotDirty_DoesNotWriteSnapshot()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);
        var service = new AutosaveService(store);
        var source = new StubSource(dirty: false, generation: 1);

        service.Attach(source, "test-nodirty-w0");
        service.OnTimerTick();

        File.Exists(store.GetSnapshotPath("test-nodirty-w0")).Should().BeFalse();
    }

    [Fact]
    public void OnTimerTick_WhenGenerationUnchanged_DoesNotWriteSecondSnapshot()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);
        var service = new AutosaveService(store);
        var source = new StubSource(dirty: true, generation: 1);

        service.Attach(source, "test-gen-w0");
        service.OnTimerTick(); // writes snapshot, records generation=1
        var snapshotWriteTime1 = File.GetLastWriteTimeUtc(store.GetSnapshotPath("test-gen-w0"));

        // Same generation — should not re-write.
        service.OnTimerTick();
        var snapshotWriteTime2 = File.GetLastWriteTimeUtc(store.GetSnapshotPath("test-gen-w0"));

        snapshotWriteTime1.Should().Be(snapshotWriteTime2, "no second write when generation unchanged");
    }

    [Fact]
    public void OnTimerTick_WhenGenerationAdvances_WritesAnotherSnapshot()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);
        var service = new AutosaveService(store);
        var source = new StubSource(dirty: true, generation: 1);

        service.Attach(source, "test-adv-w0");
        service.OnTimerTick();
        var time1 = File.GetLastWriteTimeUtc(store.GetSnapshotPath("test-adv-w0"));

        // Advance generation — simulate another edit.
        System.Threading.Thread.Sleep(10); // ensure file timestamp differs
        source.WorkbookDirtyGeneration = 2;
        service.OnTimerTick();
        var time2 = File.GetLastWriteTimeUtc(store.GetSnapshotPath("test-adv-w0"));

        time2.Should().BeAfter(time1);
    }

    [Fact]
    public void DeleteSnapshot_RemovesFiles()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);
        var service = new AutosaveService(store);
        var source = new StubSource(dirty: true, generation: 1);

        service.Attach(source, "test-del-w0");
        service.OnTimerTick();

        File.Exists(store.GetSnapshotPath("test-del-w0")).Should().BeTrue();

        service.DeleteSnapshot();

        File.Exists(store.GetSnapshotPath("test-del-w0")).Should().BeFalse();
        File.Exists(store.GetSidecarPath("test-del-w0")).Should().BeFalse();
    }

    [Fact]
    public void TryEmergencySnapshot_WhenDirty_WritesSnapshotWithoutThrowing()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);
        var service = new AutosaveService(store);
        service.Attach(new StubSource(), "emergency-placeholder");

        var source = new StubSource(dirty: true, generation: 7, filePath: @"C:\work.xlsx", name: "work");

        var act = () => service.TryEmergencySnapshot(source);
        act.Should().NotThrow();

        File.Exists(store.GetSnapshotPath("emergency-placeholder")).Should().BeTrue();
        var sidecarJson = File.ReadAllText(store.GetSidecarPath("emergency-placeholder"));
        var sidecar = AutosaveSnapshotStore.TryDeserializeSidecar(sidecarJson);
        sidecar.Should().NotBeNull();
        sidecar!.OriginalFilePath.Should().Be(@"C:\work.xlsx");
        sidecar.DisplayName.Should().Be("work");
    }

    [Fact]
    public void Dispose_PreventsSubsequentTicks()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);
        var service = new AutosaveService(store);
        var source = new StubSource(dirty: true, generation: 1);

        service.Attach(source, "test-dispose-w0");
        service.Dispose();
        service.OnTimerTick(); // should be a no-op

        File.Exists(store.GetSnapshotPath("test-dispose-w0")).Should().BeFalse();
    }
}
