using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;
using System.IO;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R91-io-file-recovery-autosave-5-1: WorkbookSaveService.SaveAsync forwarded no
/// expectedLastWriteTimeUtc to WorkbookSaveService, so the externally-modified-file
/// save-conflict guard could never fire on the real App.Host save path -- a save would
/// silently overwrite a file another program had changed on disk since it was opened.
/// These tests exercise WorkbookSaveService.SaveAsync itself (the real host save entry point),
/// not WorkbookSaveService directly, so they also guard against the parameter being dropped
/// again at this exact choke point.
/// </summary>
public sealed class R91_WorkbookSaveServiceExternalModificationTests
{
    [Fact]
    public async Task SaveAsync_FileChangedSinceCapturedTimestamp_ThrowsAndPreservesOnDiskFile()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "conflict.fxjson");
        // Simulate "opened at T0" by capturing a stale write time, then writing the file
        // "externally" (as another program/instance would) so the on-disk time no longer matches.
        await File.WriteAllTextAsync(tempPath, "external writer's content");
        var staleExpectedWriteTimeUtc = File.GetLastWriteTimeUtc(tempPath) - TimeSpan.FromMinutes(5);

        var workbook = new Workbook("Saved");
        workbook.AddSheet("Sheet1");
        var adapterInvoked = false;
        var adapter = new TestFileAdapter(save: (_, _) => adapterInvoked = true);
        var saver = new WorkbookSaveService();

        var act = async () => await saver.SaveAsync(
            tempPath,
            adapter,
            workbook,
            new TestProgress<WorkbookSaveProgressUpdate>(_ => { }),
            CancellationToken.None,
            staleExpectedWriteTimeUtc);

        await act.Should().ThrowAsync<WorkbookExternallyModifiedException>();
        adapterInvoked.Should().BeFalse();
        (await File.ReadAllTextAsync(tempPath)).Should().Be("external writer's content");
    }

    [Fact]
    public async Task SaveAsync_FileUnchangedSinceCapturedTimestamp_SavesNormally()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "no-conflict.fxjson");
        await File.WriteAllTextAsync(tempPath, "original content");
        var expectedWriteTimeUtc = File.GetLastWriteTimeUtc(tempPath);

        var workbook = new Workbook("Saved");
        workbook.AddSheet("Sheet1");
        var adapter = new TestFileAdapter(save: (_, stream) =>
        {
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write("saved payload");
        });
        var saver = new WorkbookSaveService();

        await saver.SaveAsync(
            tempPath,
            adapter,
            workbook,
            new TestProgress<WorkbookSaveProgressUpdate>(_ => { }),
            CancellationToken.None,
            expectedWriteTimeUtc);

        (await File.ReadAllTextAsync(tempPath)).Should().Be("saved payload");
    }

    [Fact]
    public async Task SaveAsync_NoExpectedTimestampSupplied_SavesNormally_NoRegression()
    {
        // No-regression sibling: omitting expectedLastWriteTimeUtc (new workbook, Save As to a
        // fresh path, or any other caller not tracking an open-time snapshot) must behave exactly
        // as before this fix -- the guard stays off and the save proceeds unconditionally.
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "untracked.fxjson");
        await File.WriteAllTextAsync(tempPath, "stale content nobody is tracking");

        var workbook = new Workbook("Saved");
        workbook.AddSheet("Sheet1");
        var adapter = new TestFileAdapter(save: (_, stream) =>
        {
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write("saved payload");
        });
        var saver = new WorkbookSaveService();

        await saver.SaveAsync(
            tempPath,
            adapter,
            workbook,
            new TestProgress<WorkbookSaveProgressUpdate>(_ => { }));

        (await File.ReadAllTextAsync(tempPath)).Should().Be("saved payload");
    }
}
