using System.Collections.Concurrent;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookOpenServiceTests
{
    [Fact]
    public async Task LoadAsync_LoadsRecalculatesAndReportsPortableProgress()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "formula-load.fxjson");
        await File.WriteAllTextAsync(tempPath, "payload");

        var adapter = new TestFileAdapter(stream =>
        {
            using var reader = new StreamReader(stream);
            reader.ReadToEnd().Should().Be("payload");
            var workbook = new Workbook("Loaded");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromFormula("1+1"));
            return workbook;
        });
        var progressUpdates = new List<WorkbookOpenProgressUpdate>();
        var recalculateCalled = false;
        var service = new WorkbookOpenService(workbook =>
        {
            workbook.Name.Should().Be("Loaded");
            recalculateCalled = true;
        });

        var result = await service.LoadAsync(
            tempPath,
            adapter,
            ".fxjson",
            new FileFormatDescriptor(".fxjson", "Fake"),
            new TestProgress<WorkbookOpenProgressUpdate>(progressUpdates.Add));

        result.Workbook.Name.Should().Be("Loaded");
        result.DisplayName.Should().Be("formula-load");
        result.FeatureReport.Should().BeNull();
        result.OpenedAsTemplate.Should().BeFalse();
        result.LoadWarnings.Should().BeEmpty();
        recalculateCalled.Should().BeTrue();
        progressUpdates.Should().Contain(update =>
            update.Phase == WorkbookOpenPhase.Reading &&
            update.Percent == 8);
        progressUpdates.Should().Contain(update =>
            update.Phase == WorkbookOpenPhase.Parsing &&
            update.Percent == 16);
        progressUpdates.Should().Contain(update =>
            update.Phase == WorkbookOpenPhase.Calculating &&
            update.Percent == 98);
    }

    [Fact]
    public async Task LoadAsync_SlowParseReportsIndeterminateProgressWhenRemainingWorkIsUnknown()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "slow-load.fxjson");
        await File.WriteAllTextAsync(tempPath, "payload");
        using var indeterminateParsingObserved = new ManualResetEventSlim();
        var progressUpdates = new ConcurrentQueue<WorkbookOpenProgressUpdate>();
        var adapter = new TestFileAdapter(stream =>
        {
            indeterminateParsingObserved.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
            using var reader = new StreamReader(stream);
            reader.ReadToEnd().Should().Be("payload");
            var workbook = new Workbook("Loaded");
            workbook.AddSheet("Sheet1");
            return workbook;
        });
        var service = new WorkbookOpenService();

        await service.LoadAsync(
            tempPath,
            adapter,
            ".fxjson",
            new FileFormatDescriptor(".fxjson", "Fake"),
            new TestProgress<WorkbookOpenProgressUpdate>(update =>
            {
                progressUpdates.Enqueue(update);
                if (update.Phase == WorkbookOpenPhase.Parsing && update.Percent is null)
                    indeterminateParsingObserved.Set();
            }));

        progressUpdates.Should().Contain(update =>
            update.Phase == WorkbookOpenPhase.Parsing &&
            update.Percent == null);
    }

    [Fact]
    public async Task LoadAsync_CanceledBeforeLoad_DoesNotInvokeAdapter()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "canceled-load.fxjson");
        await File.WriteAllTextAsync(tempPath, "payload");
        var adapterInvoked = false;
        var adapter = new TestFileAdapter(_ =>
        {
            adapterInvoked = true;
            return new Workbook("Loaded");
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = async () => await new WorkbookOpenService().LoadAsync(
            tempPath,
            adapter,
            ".fxjson",
            new FileFormatDescriptor(".fxjson", "Fake"),
            cancellationToken: cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        adapterInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_RejectsOversizedFilesBeforeLoading()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "oversized.fxjson");
        await File.WriteAllTextAsync(tempPath, "payload-that-is-too-large");
        var adapterInvoked = false;
        var adapter = new TestFileAdapter(_ =>
        {
            adapterInvoked = true;
            return new Workbook("Loaded");
        });
        var service = new WorkbookOpenService(maxFileBytes: 4);

        var act = async () => await service.LoadAsync(
            tempPath,
            adapter,
            ".fxjson",
            new FileFormatDescriptor(".fxjson", "Fake"));

        await act.Should().ThrowAsync<WorkbookTooLargeException>();
        adapterInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_CapturesSourceLastWriteTimeUtcMatchingTheFileOnDisk()
    {
        // R83-services-doc-recovery-props-5-2: WorkbookOpenService never recorded anything a later
        // save could use to detect a concurrent second writer having changed the file in the
        // meantime. LoadAsync must now snapshot the source file's write time into the result so
        // WorkbookSaveService.SaveAsync can be given it as expectedLastWriteTimeUtc.
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "formula-load.fxjson");
        await File.WriteAllTextAsync(tempPath, "payload");
        var expectedWriteTimeUtc = File.GetLastWriteTimeUtc(tempPath);
        var adapter = new TestFileAdapter(stream =>
        {
            using var reader = new StreamReader(stream);
            reader.ReadToEnd();
            var workbook = new Workbook("Loaded");
            workbook.AddSheet("Sheet1");
            return workbook;
        });

        var result = await new WorkbookOpenService().LoadAsync(
            tempPath,
            adapter,
            ".fxjson",
            new FileFormatDescriptor(".fxjson", "Fake"));

        result.SourceLastWriteTimeUtc.Should().Be(expectedWriteTimeUtc);
    }

    [Fact]
    public async Task LoadAsync_StillReturnsCorrectWorkbookAndDisplayNameAlongsideTheNewTimestamp()
    {
        // No-regression sibling: adding SourceLastWriteTimeUtc must not disturb the rest of the
        // existing WorkbookOpenResult projection.
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "formula-load.fxjson");
        await File.WriteAllTextAsync(tempPath, "payload");
        var adapter = new TestFileAdapter(stream =>
        {
            using var reader = new StreamReader(stream);
            reader.ReadToEnd().Should().Be("payload");
            var workbook = new Workbook("Loaded");
            workbook.AddSheet("Sheet1");
            return workbook;
        });

        var result = await new WorkbookOpenService().LoadAsync(
            tempPath,
            adapter,
            ".fxjson",
            new FileFormatDescriptor(".fxjson", "Fake"));

        result.Workbook.Name.Should().Be("Loaded");
        result.DisplayName.Should().Be("formula-load");
        result.OpenedAsTemplate.Should().BeFalse();
        result.LoadWarnings.Should().BeEmpty();
    }

    [Fact]
    public async Task R119_LoadAsync_CanceledDuringParsing_ReturnsPromptlyInsteadOfWaitingForParseToFinish()
    {
        // R119-appservices-open-cancel-eager, through the real entry point (WorkbookOpenService
        // .LoadAsync): before the fix, cancelling mid-parse only took effect once the (synchronous,
        // uncancellable) adapter.Load call returned on its own -- indefinitely, for a parse stuck on
        // an unresponsive network path. This proves LoadAsync itself, not just the shared runner,
        // now returns as soon as cancellation is requested.
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "slow-load.fxjson");
        await File.WriteAllTextAsync(tempPath, "payload");
        using var parseStarted = new ManualResetEventSlim();
        using var releaseParse = new ManualResetEventSlim();
        using var parseFinished = new ManualResetEventSlim();
        var adapter = new TestFileAdapter(stream =>
        {
            parseStarted.Set();
            // Bounded so the background thread always finishes (and closes its FileStream) rather
            // than leaking an indefinitely-blocked thread-pool thread past the end of this test.
            releaseParse.Wait(TimeSpan.FromSeconds(10));
            var workbook = new Workbook("Loaded");
            workbook.AddSheet("Sheet1");
            parseFinished.Set();
            return workbook;
        });
        using var cancellation = new CancellationTokenSource();

        var loadTask = new WorkbookOpenService().LoadAsync(
            tempPath,
            adapter,
            ".fxjson",
            new FileFormatDescriptor(".fxjson", "Fake"),
            cancellationToken: cancellation.Token);

        parseStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue("the adapter's Load must have started");
        cancellation.Cancel();

        var act = async () => await loadTask;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await act.Should().ThrowAsync<OperationCanceledException>();
        // Generous bound: this only needs to prove LoadAsync returned long before the 10-second
        // block releases (never, under the pre-fix behavior, since nothing ever set releaseParse
        // early) -- not race a tight deadline against thread-pool scheduling latency under a busy
        // parallel test run.
        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(5),
            "cancellation must be observed immediately instead of waiting for the still-running parse to finish");

        // Let the abandoned background parse finish (and its FileStream close) before this method's
        // `using var temp` disposal tries to recursively delete the directory that file lives in.
        releaseParse.Set();
        parseFinished.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("the abandoned background parse must still complete on its own");
        // The outer WorkbookOpenService lambda disposes its FileStream immediately after the
        // adapter callback returns, but on the background thread -- give that a brief window to
        // land before this method's `using` block tries to delete the file out from under it.
        SpinWaitUntilFileIsDeletable(tempPath);
    }

    private static void SpinWaitUntilFileIsDeletable(string path)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(20);
            }
        }
    }

    [Fact]
    public void WorkbookFormulaScanner_UsesSheetFormulaCountsInsteadOfScanningCells()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Services", "WorkbookFormulaScanner.cs"));

        source.Should().Contain("sheet.HasFormulas");
        source.Should().NotContain("EnumerateCells");
        source.Should().NotContain(".Any(");
    }
}
