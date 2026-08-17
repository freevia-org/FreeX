using System.IO;
using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// FreeP had no autosave machinery at all before this round: no periodic snapshot, no emergency
/// snapshot, and no startup recovery offer. These tests cover the renderer-neutral engine binding
/// (<see cref="FreePAutosaveSession"/>) both shells now schedule.
/// </summary>
public sealed class FreePAutosaveSessionTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("freep-autosave-session-");
    private string TempDirectory => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    private static FreePAutosavePorts Ports(
        Presentation presentation,
        bool isDirty,
        int dirtyGeneration = 1,
        string? originalFilePath = null,
        string displayName = "Presentation1") =>
        new(
            GetOriginalFilePath: () => originalFilePath,
            GetDisplayName: () => displayName,
            GetIsDirty: () => isDirty,
            GetDirtyGeneration: () => dirtyGeneration,
            ExecuteWithPresentation: write => write(presentation));

    [Fact]
    public void Snapshot_WritesAReadablePptxPackageAndSidecarForADirtyPresentation()
    {
        var store = new AutosaveSnapshotStore(TempDirectory);
        using var session = new FreePAutosaveSession(
            Ports(Presentation.CreateEmpty(), isDirty: true, originalFilePath: @"C:\decks\Quarterly.pptx"),
            store,
            "freep-autosave-write");

        session.Snapshot();

        var snapshotPath = store.GetSnapshotPath("freep-autosave-write");
        File.Exists(snapshotPath).Should().BeTrue();
        File.Exists(store.GetSidecarPath("freep-autosave-write")).Should().BeTrue();

        // The snapshot must be a real .pptx package, not just bytes on disk: recovery reads it back
        // through PptxPackageReader, so an unreadable snapshot is the same as no snapshot at all.
        var reopened = PptxPackageReader.Read(snapshotPath);
        reopened.Slides.Should().HaveCount(Presentation.CreateEmpty().Slides.Count);
    }

    [Fact]
    public void Snapshot_SkipsACleanPresentation()
    {
        var store = new AutosaveSnapshotStore(TempDirectory);
        using var session = new FreePAutosaveSession(
            Ports(Presentation.CreateEmpty(), isDirty: false),
            store,
            "freep-autosave-clean");

        session.Snapshot();

        File.Exists(store.GetSnapshotPath("freep-autosave-clean")).Should().BeFalse();
    }

    /// <summary>
    /// The emergency path bypasses the periodic-tick generation gate -- it must still capture the
    /// latest dirty state when nothing changed since the last tick -- but must never resurrect a
    /// presentation that had nothing unsaved at crash time.
    /// </summary>
    [Fact]
    public void TryEmergencySnapshot_WritesForADirtyPresentationWithoutWaitingForATick()
    {
        var store = new AutosaveSnapshotStore(TempDirectory);
        using var session = new FreePAutosaveSession(
            Ports(Presentation.CreateEmpty(), isDirty: true),
            store,
            "freep-autosave-emergency");

        session.TryEmergencySnapshot();

        File.Exists(store.GetSnapshotPath("freep-autosave-emergency")).Should().BeTrue();
    }

    [Fact]
    public void TryEmergencySnapshot_SkipsACleanPresentation()
    {
        var store = new AutosaveSnapshotStore(TempDirectory);
        using var session = new FreePAutosaveSession(
            Ports(Presentation.CreateEmpty(), isDirty: false),
            store,
            "freep-autosave-emergency-clean");

        session.TryEmergencySnapshot();

        File.Exists(store.GetSnapshotPath("freep-autosave-emergency-clean")).Should().BeFalse();
    }

    /// <summary>
    /// A crash handler must never throw. A port that blows up mid-serialization has to degrade to
    /// "no snapshot", not to an exception escaping the crash handler.
    /// </summary>
    [Fact]
    public void TryEmergencySnapshot_SwallowsSerializationFailures()
    {
        var store = new AutosaveSnapshotStore(TempDirectory);
        using var session = new FreePAutosaveSession(
            new FreePAutosavePorts(
                GetOriginalFilePath: () => null,
                GetDisplayName: () => "Presentation1",
                GetIsDirty: () => true,
                GetDirtyGeneration: () => 1,
                ExecuteWithPresentation: _ => throw new InvalidOperationException("boom")),
            store,
            "freep-autosave-throwing");

        var act = session.TryEmergencySnapshot;

        act.Should().NotThrow();
    }

    [Fact]
    public void CompleteCleanExit_DeletesTheSnapshot()
    {
        var store = new AutosaveSnapshotStore(TempDirectory);
        var session = new FreePAutosaveSession(
            Ports(Presentation.CreateEmpty(), isDirty: true),
            store,
            "freep-autosave-clean-exit");
        session.Snapshot();
        File.Exists(store.GetSnapshotPath("freep-autosave-clean-exit")).Should().BeTrue();

        session.CompleteCleanExit();

        File.Exists(store.GetSnapshotPath("freep-autosave-clean-exit")).Should().BeFalse();
        File.Exists(store.GetSidecarPath("freep-autosave-clean-exit")).Should().BeFalse();
    }

    /// <summary>
    /// A recovered snapshot is unsaved work belonging to the ORIGINAL file, so it must come back
    /// through the sidecar's original path rather than the snapshot's throwaway location.
    /// </summary>
    [Fact]
    public void CompletePresentationRecovery_HandsBackTheDeckAndItsOriginalPath()
    {
        var store = new AutosaveSnapshotStore(TempDirectory);
        var originalPath = Path.Combine(TempDirectory, "Quarterly.pptx");
        var writer = new FreePAutosaveSession(
            Ports(Presentation.CreateEmpty(), isDirty: true, originalFilePath: originalPath),
            store,
            "freep-autosave-recovery");
        writer.Snapshot();
        writer.Dispose(); // release the ownership lock so the candidate is offerable

        using var reader = new FreePAutosaveSession(
            Ports(Presentation.CreateEmpty(), isDirty: false),
            store,
            "freep-autosave-recovery-reader");
        var plan = reader.PlanLatestRecovery();
        plan.Should().NotBeNull();

        Presentation? recovered = null;
        string? recoveredPath = null;
        var result = reader.CompletePresentationRecovery(
            plan!,
            accepted: true,
            (presentation, path) => { recovered = presentation; recoveredPath = path; });

        result.Should().BeTrue();
        recovered.Should().NotBeNull();
        recoveredPath.Should().Be(originalPath);
    }
}
