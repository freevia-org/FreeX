using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSaveServiceTests
{
    [Fact]
    public async Task SaveAsync_WritesWorkbookThroughTemporaryFileAndReportsPortableProgress()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "saved.fxjson");
        var workbook = new Workbook("Saved");
        workbook.AddSheet("Sheet1");
        var adapter = new TestFileAdapter(save: (savedWorkbook, stream) =>
        {
            savedWorkbook.Should().BeSameAs(workbook);
            stream.Should().BeOfType<FileStream>();
            stream.CanRead.Should().BeTrue();
            stream.CanSeek.Should().BeTrue();
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write("saved payload");
        });
        var progressUpdates = new List<WorkbookSaveProgressUpdate>();

        await new WorkbookSaveService().SaveAsync(
            tempPath,
            adapter,
            workbook,
            new TestProgress<WorkbookSaveProgressUpdate>(progressUpdates.Add));

        (await File.ReadAllTextAsync(tempPath)).Should().Be("saved payload");
        progressUpdates.Should().Contain(update =>
            update.Phase == WorkbookSavePhase.Preparing &&
            update.Percent == 1);
        progressUpdates.Should().Contain(update =>
            update.Phase == WorkbookSavePhase.Writing &&
            update.Percent == 99);
        progressUpdates.Should().Contain(update =>
            update.Phase == WorkbookSavePhase.Completed &&
            update.Percent == 100);
        Directory.GetFiles(temp.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_ReplacesExistingFileThroughTemporaryFile()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "saved.fxjson");
        await File.WriteAllTextAsync(tempPath, "original");
        var workbook = new Workbook("Saved");
        workbook.AddSheet("Sheet1");
        var adapter = new TestFileAdapter(save: (_, stream) =>
        {
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write("replacement");
        });

        await new WorkbookSaveService().SaveAsync(tempPath, adapter, workbook);

        (await File.ReadAllTextAsync(tempPath)).Should().Be("replacement");
        Directory.GetFiles(temp.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_FallsBackToMoveReplacementWhenFileReplaceIsUnsupported()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "saved.fxjson");
        await File.WriteAllTextAsync(tempPath, "original");
        var workbook = new Workbook("Saved");
        workbook.AddSheet("Sheet1");
        var adapter = new TestFileAdapter(save: (_, stream) =>
        {
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write("replacement");
        });
        var fileOperations = new TestWorkbookSaveFileOperations
        {
            ReplaceException = new IOException(
                "Operation not supported",
                unchecked((int)0x8007002D))
        };

        await new WorkbookSaveService(fileOperations).SaveAsync(tempPath, adapter, workbook);

        (await File.ReadAllTextAsync(tempPath)).Should().Be("replacement");
        fileOperations.ReplaceCallCount.Should().Be(1);
        fileOperations.OverwriteMoveCallCount.Should().Be(1);
        Directory.GetFiles(temp.Path, "*.tmp").Should().BeEmpty();
        Directory.GetFiles(temp.Path, "*.bak").Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_PreservesExistingFileAndDeletesTemporaryFileWhenFallbackMoveFails()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "saved.fxjson");
        await File.WriteAllTextAsync(tempPath, "original");
        var workbook = new Workbook("Saved");
        workbook.AddSheet("Sheet1");
        var adapter = new TestFileAdapter(save: (_, stream) =>
        {
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write("replacement");
        });
        var fileOperations = new TestWorkbookSaveFileOperations
        {
            ReplaceException = new PlatformNotSupportedException("replace unsupported"),
            OverwriteMoveException = new IOException("move failed")
        };

        var act = async () => await new WorkbookSaveService(fileOperations).SaveAsync(tempPath, adapter, workbook);

        await act.Should().ThrowAsync<IOException>().WithMessage("move failed");
        (await File.ReadAllTextAsync(tempPath)).Should().Be("original");
        fileOperations.ReplaceCallCount.Should().Be(1);
        fileOperations.OverwriteMoveCallCount.Should().Be(1);
        Directory.GetFiles(temp.Path, "*.tmp").Should().BeEmpty();
        Directory.GetFiles(temp.Path, "*.bak").Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_DoesNotFallbackForOrdinaryFileReplaceFailures()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "saved.fxjson");
        await File.WriteAllTextAsync(tempPath, "original");
        var workbook = new Workbook("Saved");
        workbook.AddSheet("Sheet1");
        var adapter = new TestFileAdapter(save: (_, stream) =>
        {
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write("replacement");
        });
        var fileOperations = new TestWorkbookSaveFileOperations
        {
            ReplaceException = new IOException(
                "sharing violation",
                unchecked((int)0x80070020))
        };

        var act = async () => await new WorkbookSaveService(fileOperations).SaveAsync(tempPath, adapter, workbook);

        await act.Should().ThrowAsync<IOException>().WithMessage("sharing violation");
        (await File.ReadAllTextAsync(tempPath)).Should().Be("original");
        fileOperations.ReplaceCallCount.Should().Be(1);
        fileOperations.OverwriteMoveCallCount.Should().Be(0);
        Directory.GetFiles(temp.Path, "*.tmp").Should().BeEmpty();
        Directory.GetFiles(temp.Path, "*.bak").Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_PreservesExistingFileAndDeletesTemporaryFileWhenAdapterFails()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "saved.fxjson");
        await File.WriteAllTextAsync(tempPath, "original");
        var workbook = new Workbook("Saved");
        workbook.AddSheet("Sheet1");
        var adapter = new TestFileAdapter(save: (_, _) => throw new InvalidOperationException("boom"));

        var act = async () => await new WorkbookSaveService().SaveAsync(tempPath, adapter, workbook);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await File.ReadAllTextAsync(tempPath)).Should().Be("original");
        Directory.GetFiles(temp.Path, "*.tmp").Should().BeEmpty();
    }

    private sealed class TestWorkbookSaveFileOperations : IWorkbookSaveFileOperations
    {
        public Exception? ReplaceException { get; init; }

        public Exception? OverwriteMoveException { get; init; }

        public int ReplaceCallCount { get; private set; }

        public int OverwriteMoveCallCount { get; private set; }

        public bool FileExists(string path) => File.Exists(path);

        public void ReplaceFile(string sourcePath, string destinationPath)
        {
            ReplaceCallCount++;
            if (ReplaceException is not null)
                throw ReplaceException;

            File.Replace(sourcePath, destinationPath, null, ignoreMetadataErrors: true);
        }

        public void MoveFile(string sourcePath, string destinationPath, bool overwrite)
        {
            if (overwrite)
            {
                OverwriteMoveCallCount++;
                if (OverwriteMoveException is not null)
                    throw OverwriteMoveException;

                File.Move(sourcePath, destinationPath, overwrite: true);
            }
            else
            {
                File.Move(sourcePath, destinationPath);
            }
        }

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite) =>
            File.Copy(sourcePath, destinationPath, overwrite);

        public void DeleteFile(string path) => File.Delete(path);
    }
}
