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
        public string DocumentId => Workbook.Id.Value.ToString();
    }

    [Fact]
    public void DefaultInterval_RemainsFiveMinutes()
    {
        AutosaveService.DefaultInterval.Should().Be(TimeSpan.FromMinutes(5));
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
    public void Attach_WithWindowId_UsesCanonicalPerLaunchSnapshotIdentity()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);
        var service = new AutosaveService(store);
        var source = new StubSource(dirty: true, generation: 1);
        var windowId = Guid.Parse("12345678-90ab-cdef-1234-567890abcdef");

        service.Attach(source, windowId);
        service.OnTimerTick();

        var launchTag = AutosaveSnapshotStore.LaunchId.ToString("N")[..8];
        var expectedId = FormattableString.Invariant(
            $"recovery-{Environment.ProcessId}-{launchTag}-12345678");
        File.Exists(store.GetSnapshotPath(expectedId)).Should().BeTrue();
        File.Exists(store.GetSidecarPath(expectedId)).Should().BeTrue();
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
    public void TryEmergencySnapshot_WithoutSource_UsesAttachedWorkbook()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);
        var service = new AutosaveService(store);
        var source = new StubSource(dirty: true, generation: 7, filePath: @"C:\work.xlsx", name: "work");
        service.Attach(source, "emergency-bound-source");

        Action act = service.TryEmergencySnapshot;

        act.Should().NotThrow();
        File.Exists(store.GetSnapshotPath("emergency-bound-source")).Should().BeTrue();
        var sidecarJson = File.ReadAllText(store.GetSidecarPath("emergency-bound-source"));
        var sidecar = AutosaveSnapshotStore.TryDeserializeSidecar(sidecarJson);
        sidecar.Should().NotBeNull();
        sidecar!.DocumentId.Should().Be(source.DocumentId);
    }

    [Fact]
    public void TryEmergencySnapshot_WhenNotDirty_DoesNotWriteSnapshot()
    {
        // R74-services-autosave-recovery-4-2: an emergency crash-handler snapshot bypasses the
        // GENERATION gate (so it always tries to capture the latest state), but must still honor
        // the underlying dirty check. A clean (IsDirty=false) workbook has no unsaved changes to
        // recover, and Excel never offers Document Recovery for a document that was clean at
        // crash time — so no snapshot (and later no recovery offer) should be produced for it.
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);
        var service = new AutosaveService(store);
        service.Attach(new StubSource(), "emergency-clean-placeholder");

        var source = new StubSource(dirty: false, generation: 7, filePath: @"C:\work.xlsx", name: "work");

        var act = () => service.TryEmergencySnapshot(source);
        act.Should().NotThrow();

        File.Exists(store.GetSnapshotPath("emergency-clean-placeholder")).Should().BeFalse(
            "a clean workbook has nothing unsaved to recover, so an emergency snapshot must not be written for it");
        File.Exists(store.GetSidecarPath("emergency-clean-placeholder")).Should().BeFalse();
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

    [Fact]
    public void Dispose_PreventsBoundEmergencySnapshot()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);
        var service = new AutosaveService(store);
        var source = new StubSource(dirty: true, generation: 1);

        service.Attach(source, "test-dispose-emergency");
        service.Dispose();
        service.TryEmergencySnapshot();

        File.Exists(store.GetSnapshotPath("test-dispose-emergency")).Should().BeFalse();
        File.Exists(store.GetSidecarPath("test-dispose-emergency")).Should().BeFalse();
    }
}
