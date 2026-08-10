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
        var cups = ReadSource("src", "FreeX.App.Avalonia", "CupsPlatformPrinter.cs");
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

        cups.Should().Contain("TemporaryFileLease.Create(\"freex-print-\", \".pdf\")");
        cups.Should().NotContain("Path.GetTempPath()");
        cups.Should().NotContain("private static void TryDelete");

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
