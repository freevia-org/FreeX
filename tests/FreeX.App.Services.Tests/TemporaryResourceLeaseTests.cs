using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Services.Tests;

public sealed class TemporaryResourceLeaseTests
{
    [Fact]
    public void FileLease_ReservesWritesAndReleasesIdempotently()
    {
        using var root = new TestTemporaryDirectory(nameof(FileLease_ReservesWritesAndReleasesIdempotently));
        var lease = TemporaryFileLease.Create("freex-print-", ".pdf", root.Path);

        File.Exists(lease.Path).Should().BeTrue();
        Path.GetFileName(lease.Path).Should().StartWith("freex-print-").And.EndWith(".pdf");
        lease.WriteAllBytes([1, 2, 3]);
        File.ReadAllBytes(lease.Path).Should().Equal(1, 2, 3);

        lease.Release();
        lease.Release();
        lease.Dispose();

        File.Exists(lease.Path).Should().BeFalse();
        lease.OwnsResource.Should().BeFalse();
    }

    [Fact]
    public void FileLease_DeterministicallyRetriesAReservedName()
    {
        using var root = new TestTemporaryDirectory(nameof(FileLease_DeterministicallyRetriesAReservedName));
        File.WriteAllText(Path.Combine(root.Path, "job-collision.tmp"), "occupied");
        var tokens = new Queue<string>(["collision", "available"]);

        using var lease = TemporaryFileLease.Create(
            "job-",
            ".tmp",
            root.Path,
            uniqueTokenFactory: tokens.Dequeue);

        Path.GetFileName(lease.Path).Should().Be("job-available.tmp");
        File.Exists(lease.Path).Should().BeTrue();
    }

    [Fact]
    public void FileLease_ForExternalWriterRetainsCleanupOwnershipWithoutLeavingAPlaceholder()
    {
        using var root = new TestTemporaryDirectory(nameof(FileLease_ForExternalWriterRetainsCleanupOwnershipWithoutLeavingAPlaceholder));
        var lease = TemporaryFileLease.CreateForExternalWriter(
            "native-",
            ".wav",
            root.Path,
            uniqueTokenFactory: () => "capture");

        File.Exists(lease.Path).Should().BeFalse();
        File.WriteAllBytes(lease.Path, [1, 2, 3]);

        lease.Dispose();

        File.Exists(lease.Path).Should().BeFalse();
        lease.OwnsResource.Should().BeFalse();
    }

    [Fact]
    public void FileLease_ConcurrentCreatorsReserveDistinctFilesAndCleanThemAll()
    {
        using var root = new TestTemporaryDirectory(nameof(FileLease_ConcurrentCreatorsReserveDistinctFilesAndCleanThemAll));
        var leases = new TemporaryFileLease?[64];

        Parallel.For(
            0,
            leases.Length,
            index => leases[index] = TemporaryFileLease.Create("parallel-", ".bin", root.Path));

        leases.All(lease => lease is not null).Should().BeTrue();
        leases.Select(lease => lease!.Path).Should().OnlyHaveUniqueItems();
        leases.Should().OnlyContain(lease => File.Exists(lease!.Path));

        Parallel.ForEach(leases, lease => lease!.Release());

        Directory.EnumerateFileSystemEntries(root.Path).Should().BeEmpty();
    }

    [Fact]
    public void FileLease_SuppliedPathCanBeCommittedOrOwnedForCleanup()
    {
        using var root = new TestTemporaryDirectory(nameof(FileLease_SuppliedPathCanBeCommittedOrOwnedForCleanup));
        var path = Path.Combine(root.Path, "supplied.tmp");

        using (var reserved = TemporaryFileLease.Reserve(path))
        {
            reserved.WriteAllBytes([4, 5]);
            reserved.Commit();
        }

        File.ReadAllBytes(path).Should().Equal(4, 5);

        using (TemporaryFileLease.Own(path))
        {
        }

        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void DirectoryLease_ReservesRecursivelyCleansAndCanBeKept()
    {
        using var root = new TestTemporaryDirectory(nameof(DirectoryLease_ReservesRecursivelyCleansAndCanBeKept));
        string releasedPath;
        using (var lease = TemporaryDirectoryLease.Create("render-", root.Path))
        {
            releasedPath = lease.Path;
            Directory.CreateDirectory(Path.Combine(lease.Path, "nested"));
            File.WriteAllText(Path.Combine(lease.Path, "nested", "frame.bin"), "payload");
        }

        Directory.Exists(releasedPath).Should().BeFalse();

        var keptPath = Path.Combine(root.Path, "kept");
        using (var kept = TemporaryDirectoryLease.Reserve(keptPath))
            kept.Keep();

        Directory.Exists(keptPath).Should().BeTrue();
    }

    [Fact]
    public void Release_SwallowsCleanupFailureAndAttemptsItOnlyOnce()
    {
        using var root = new TestTemporaryDirectory(nameof(Release_SwallowsCleanupFailureAndAttemptsItOnlyOnce));
        var fileSystem = new DeleteFailingFileSystem(root.Path);
        var lease = TemporaryFileLease.Create(
            "locked-",
            ".tmp",
            root.Path,
            fileSystem,
            () => "one");

        var act = () =>
        {
            lease.Release();
            lease.Release();
            lease.Dispose();
        };

        act.Should().NotThrow();
        fileSystem.DeleteAttempts.Should().Be(1);
        File.Exists(lease.Path).Should().BeTrue();
        File.Delete(lease.Path);
    }

    [Fact]
    public void AtomicWriter_UsesLeaseCleanupWithoutChangingReplaceSemantics()
    {
        using var root = new TestTemporaryDirectory(nameof(AtomicWriter_UsesLeaseCleanupWithoutChangingReplaceSemantics));
        var target = Path.Combine(root.Path, "report.pdf");
        File.WriteAllText(target, "old");

        AtomicFileWriter.WriteAllText(target, "new");

        File.ReadAllText(target).Should().Be("new");
        Directory.EnumerateFileSystemEntries(root.Path, ".report.pdf.*.tmp").Should().BeEmpty();

        var blockedTarget = Path.Combine(root.Path, "blocked.pdf");
        Directory.CreateDirectory(blockedTarget);
        var act = () => AtomicFileWriter.WriteAllBytes(blockedTarget, [1, 2, 3]);

        act.Should().Throw<Exception>();
        Directory.EnumerateFileSystemEntries(root.Path, ".blocked.pdf.*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void ScopedProductionFlowsDelegateTemporaryOwnershipToLeases()
    {
        var freeXPrintWorkflow = ReadSource("src", "FreeX.App.Services", "WorkbookPrintWorkflow.cs");
        var freeWOutput = ReadSource(
            "freew",
            "FreeW.App.Presentation",
            "Shell",
            "FreeWOutputWorkflow.cs");
        var media = ReadSource("freep", "FreeP.App.Media", "MediaPlaybackContracts.cs");
        var freePWindow = ReadSource("freep", "FreeP.App.Avalonia", "MainWindow.cs");
        var atomicWriter = ReadSource(
            "shared",
            "Free.Shared.AppServices",
            "AtomicFileWriter.cs");
        var freeWDocumentSave = ReadSource(
            "freew",
            "FreeW.App.Presentation",
            "Shell",
            "DocumentPersistenceWorkflow.cs");
        var freeXWpfExport = ReadSource("src", "FreeX.App.Host", "MainWindow.PrintExport.cs");

        freeXPrintWorkflow.Should().Contain("TemporaryFileLease.Create(\"freex-print-\", \".pdf\")");
        freeXPrintWorkflow.Should().Contain("temporaryFile.WriteAllBytesAsync(");
        freeXPrintWorkflow.Should().NotContain("Path.GetTempPath()");
        freeXPrintWorkflow.Should().NotContain("private static void TryDelete");

        freeWOutput.Should().Contain("ExportAtomicWriter.CreateTempLease(path)");
        freeWOutput.Should().Contain("TemporaryFileLease.Create(\"FreeW-print-\", \".pdf\")");
        freeWOutput.Should().NotContain("Path.GetTempPath()");
        freeWOutput.Should().NotContain("File.Delete(");

        media.Should().Contain("TemporaryFileLease.Create(\"freep_playback_\", extension)");
        media.Should().Contain("Dictionary<string, TemporaryFileLease>");
        media.Should().NotContain("Path.GetTempPath()");
        media.Should().NotContain("File.Delete(");

        freePWindow.Should().Contain("TemporaryFileLease.Create(\"freep-print-\", \".pdf\")");
        freePWindow.Should().NotContain("TryDeletePrintFile");
        freePWindow.Should().NotContain("$\"freep-print-{Guid.NewGuid():N}.pdf\"");

        atomicWriter.Should().Contain("using var temporaryFile = CreateTempLease(fullTargetPath);");
        atomicWriter.Should().Contain("temporaryFile.Commit();");
        atomicWriter.Should().NotContain("private static void TryDelete");

        freeWDocumentSave.Should().Contain("ExportAtomicWriter.CreateTempLease(target.Path)");
        freeWDocumentSave.Should().NotContain("ExportAtomicWriter.CreateTempPath(");
        freeXWpfExport.Should().Contain("ExportAtomicWriter.CreateTempLease(xpsPath)");
        freeXWpfExport.Should().NotContain("ExportAtomicWriter.CreateTempPath(");
    }

    [Fact]
    public void RemainingProductionTemporaryFlowsDelegateOwnershipToLeases()
    {
        var linuxOutput = ReadSource("freep", "FreeP.App.Recording", "Recording", "LinuxNativeOutput.cs");
        var videoExportOrchestrator = ReadSource(
            "freep",
            "FreeP.App.Recording",
            "Recording",
            "PresentationVideoExportOrchestrator.cs");
        var linuxCapture = ReadSource("freep", "FreeP.App.Recording", "Recording", "LinuxMediaCaptureLifecycle.cs");
        var windowsCapture = ReadSource("freep", "FreeP.App.Recording.Windows", "WindowsRecordingCaptureEngine.cs");
        var windowsVideo = ReadSource("freep", "FreeP.App.Recording.Windows", "WindowsNativeVideoExportAdapter.cs");
        var windowsPrint = ReadSource("freep", "FreeP.App.Recording.Windows", "WindowsNativePrintHandoff.cs");
        var wpfVideo = ReadSource("freep", "FreeP.App.Host", "WpfVideoExportAdapter.cs");
        var transitionSound = ReadSource("freep", "FreeP.App.Host", "TransitionSoundTempFile.cs");
        var slideShowMedia = ReadSource("freep", "FreeP.App.Host", "SlideShowMediaController.cs");
        var oleActivation = ReadSource("freep", "FreeP.App.Presentation", "OleActivationService.cs");
        var oleInPlace = ReadSource("freep", "FreeP.App.Ole.Windows", "WindowsOleInPlaceEngine.cs");
        var svgRasterizer = ReadSource("freew", "FreeW.App.Host", "SvgRasterizerHelper.cs");
        var screenClip = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "ScreenClipService.cs");
        var readAloudPauseSmoke = ReadSource(
            "freew",
            "FreeW.App.Avalonia",
            "Smoke",
            "ReadAloudPauseSmoke.cs");
        var autosave = ReadSource("shared", "Free.Shared.AppServices", "AutosaveSnapshotCoordinator.cs");

        linuxOutput.Should().Contain("TemporaryFileLease.Create(\"freep-print-\", \".pdf\")");
        linuxOutput.Should().Contain("TemporaryDirectoryPrefix: \"freep-video-\"");
        videoExportOrchestrator.Should().Contain("TemporaryDirectoryLease.Create(");
        videoExportOrchestrator.Should().Contain("_options.TemporaryDirectoryPrefix");
        linuxCapture.Should().Contain("TemporaryDirectoryLease.Create(");
        linuxCapture.Should().Contain("TemporaryFileLease.Own(outputPath)");
        windowsCapture.Should().Contain("TemporaryFileLease.CreateForExternalWriter(\"freep_rec_\", \".wav\")");
        windowsVideo.Should().Contain("TemporaryDirectoryPrefix: \"freep-windows-video-\"");
        windowsPrint.Should().Contain("TemporaryFileLease.Create(\"freep-print-\", \".pdf\")");
        wpfVideo.Should().NotContain("TemporaryDirectoryLease");
        transitionSound.Should().Contain("TemporaryFileLease.Create(\"freep_transition_\", extension)");
        slideShowMedia.Should().Contain("TemporaryFileLease.Create(\"freep_media_\", ext)");
        oleActivation.Should().Contain("TemporaryDirectoryLease.Create(string.Empty, root)");
        oleInPlace.Should().Contain("TemporaryFileLease.Create(");
        svgRasterizer.Should().Contain("TemporaryFileLease.Create(\"freew_icon_\", \".svg\")");
        screenClip.Should().Contain("TemporaryFileLease.CreateForExternalWriter(");
        readAloudPauseSmoke.Should().Contain(
            "TemporaryFileLease.CreateForExternalWriter(");
        readAloudPauseSmoke.Should().NotContain("Path.GetTempPath()");
        readAloudPauseSmoke.Should().NotContain("File.Delete(");
        autosave.Should().Contain("AtomicFileWriter.CreateTempLease(snapshotPath)");

        foreach (var source in new[]
                 {
                     linuxOutput, videoExportOrchestrator, linuxCapture, windowsCapture, windowsVideo, windowsPrint,
                     wpfVideo, transitionSound, slideShowMedia, oleInPlace, svgRasterizer, screenClip,
                     readAloudPauseSmoke,
                 })
        {
            source.Should().NotContain("private static void TryDeleteDirectory");
        }
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(RepositoryFileLocator.Find(parts));

    private sealed class DeleteFailingFileSystem(string temporaryDirectoryPath)
        : ITemporaryResourceFileSystem
    {
        public int DeleteAttempts { get; private set; }

        public string GetTemporaryDirectoryPath() => temporaryDirectoryPath;

        public bool FileExists(string path) => File.Exists(path);

        public bool DirectoryExists(string path) => Directory.Exists(path);

        public Stream CreateNewFile(string path) => new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);

        public Stream OpenFileForWrite(string path, bool useAsync, int bufferSize) => new FileStream(
            path,
            FileMode.Truncate,
            FileAccess.Write,
            FileShare.None,
            bufferSize,
            useAsync);

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public void DeleteFile(string path)
        {
            DeleteAttempts++;
            throw new IOException("simulated cleanup failure");
        }

        public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
    }
}
