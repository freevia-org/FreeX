using FluentAssertions;
using FreeX.Core.IO;
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
    public async Task SaveAsync_CanceledBeforeSave_DoesNotInvokeAdapterOrCreateTarget()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "canceled-save.fxjson");
        var workbook = new Workbook("Canceled");
        workbook.AddSheet("Sheet1");
        var adapterInvoked = false;
        var adapter = new TestFileAdapter(save: (_, _) => adapterInvoked = true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = async () => await new WorkbookSaveService().SaveAsync(
            tempPath,
            adapter,
            workbook,
            cancellationToken: cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        adapterInvoked.Should().BeFalse();
        File.Exists(tempPath).Should().BeFalse();
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
        // Fallback now vacates `path` to the backup location first (a plain rename) and then
        // places tempPath into the now-vacant `path` (a plain create, not an overwrite) -- two
        // moves total, neither of which touches live data in place. See R114_* tests below for
        // why this replaced the old single in-place overwrite move.
        fileOperations.MoveCallCount.Should().Be(2);
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
        // Fail only the FIRST move that targets `path` (the temp-into-target placement move),
        // not the later restore-from-backup move that also targets `path` -- so this still
        // exercises "the placement move throws before touching path", letting the restore run.
        var placementMoveFailed = false;
        var fileOperations = new TestWorkbookSaveFileOperations
        {
            ReplaceException = new PlatformNotSupportedException("replace unsupported"),
            MoveFileException = (_, destination, _) =>
            {
                if (placementMoveFailed ||
                    !string.Equals(Path.GetFullPath(destination), Path.GetFullPath(tempPath), StringComparison.OrdinalIgnoreCase))
                    return null;

                placementMoveFailed = true;
                return new IOException("move failed");
            }
        };

        var act = async () => await new WorkbookSaveService(fileOperations).SaveAsync(tempPath, adapter, workbook);

        await act.Should().ThrowAsync<IOException>().WithMessage("move failed");
        (await File.ReadAllTextAsync(tempPath)).Should().Be("original");
        fileOperations.ReplaceCallCount.Should().Be(1);
        // Vacate move (path -> backup) succeeds, the placement move (temp -> path) throws before
        // writing anything, then the restore move (backup -> path) puts the original back --
        // three MoveFile calls total.
        fileOperations.MoveCallCount.Should().Be(3);
        Directory.GetFiles(temp.Path, "*.tmp").Should().BeEmpty();
        Directory.GetFiles(temp.Path, "*.bak").Should().BeEmpty();
    }

    [Fact]
    public async Task R114_SaveAsync_FallbackPlacementMovePartiallyOverwritesTarget_RestoresOriginalNotCorruptRemnant()
    {
        // R114-services-workbooksave-fallback-inplace-overwrite: on filesystems where File.Move
        // can't do an atomic same-volume rename-with-replace (FAT32/exFAT, many SMB/NAS shares,
        // cloud-sync placeholder filesystems such as OneDrive Files-On-Demand -- the very
        // filesystem class this repo's own working tree lives on), the runtime falls back to a
        // byte-level copy-then-delete that can fail PARTWAY THROUGH (disk-full, permission,
        // network-drop mid-copy). This fake simulates that faithfully: the move that writes into
        // the live target path first writes a truncated/garbage payload to it (as a real partial
        // copy would) and only then throws -- unlike the pre-existing regression test above
        // (SaveAsync_PreservesExistingFileAndDeletesTemporaryFileWhenFallbackMoveFails), whose
        // fake throws BEFORE touching any bytes and so cannot exercise this failure mode at all.
        //
        // Before the fix: ReplaceExistingFileWithFallback moved the new content directly into
        // `path` via MoveFile(tempPath, path, overwrite: true) -- an in-place overwrite of live
        // data. A partial failure there left `path` holding the truncated remnant written above.
        // RestoreFallbackBackup only checked File.Exists(path) (true, for the remnant) and
        // reported "nothing to restore", so the outer finally then deleted the only pristine
        // backup -- destroying the user's file with no recovery path.
        //
        // After the fix: `path` is vacated to the backup location BEFORE the risky move is
        // attempted, so the risky move is always a plain create into an already-vacant `path`,
        // never an overwrite of live data. A partial failure leaves `path` holding, at worst, the
        // same truncated remnant -- but RestoreFallbackBackup now unconditionally discards
        // whatever is at `path` and restores the backup, because vacating first means anything
        // found at `path` after a failure can never be the still-good original.
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
            // Only the move whose SOURCE is the internal temp file and whose DESTINATION is the
            // real target path is the risky "placement" move -- the later restore move (source is
            // the backup, destination is also the real target path) must be left alone so the
            // fixed implementation can actually recover.
            PartialWriteFailureDestinationPath = tempPath
        };

        var act = async () => await new WorkbookSaveService(fileOperations).SaveAsync(tempPath, adapter, workbook);

        await act.Should().ThrowAsync<IOException>().WithMessage("*partial move failure*");
        (await File.ReadAllTextAsync(tempPath)).Should().Be(
            "original",
            "a save that fails partway through the fallback move must never leave a corrupted/truncated remnant at the target path, and must never delete the only backup of a corrupted file");
        Directory.GetFiles(temp.Path, "*.bak").Should().BeEmpty();
        Directory.GetFiles(temp.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task R114_SaveAsync_FallbackSucceeds_VacatesThenPlacesRatherThanOverwritingInPlace()
    {
        // No-regression sibling for the test above: the happy-path fallback (no failure at all)
        // must still end with the new content at `path`, no leftover temp/backup files, and must
        // still route through the vacate-then-place sequence (two moves, neither of which is an
        // in-place overwrite) rather than reverting to the old single overwrite-move mechanism.
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
            ReplaceException = new PlatformNotSupportedException("replace unsupported")
        };

        await new WorkbookSaveService(fileOperations).SaveAsync(tempPath, adapter, workbook);

        (await File.ReadAllTextAsync(tempPath)).Should().Be("replacement");
        fileOperations.MoveCallCount.Should().Be(2);
        fileOperations.OverwriteMoveCallCount.Should().Be(
            0,
            "the fallback must never call MoveFile with overwrite:true against the live target -- both the vacate and placement moves target an unoccupied destination");
        Directory.GetFiles(temp.Path, "*.tmp").Should().BeEmpty();
        Directory.GetFiles(temp.Path, "*.bak").Should().BeEmpty();
    }

    [Fact]
    public async Task R115_SaveAsync_TargetMissingWithOrphanedFallbackBackup_RecoversAndDiscardsBackup()
    {
        // R115-services-workbooksave-orphaned-fallback-backup: ReplaceExistingFileWithFallback's
        // vacate-then-place sequence vacates `path` to a hidden `.bak` sibling BEFORE placing the
        // new content back into `path` (see its own comments). If the process dies in that window
        // -- power loss, OS crash, forced task-kill -- after the vacate move completes but before
        // the placement move starts or finishes, `path` is left missing entirely while the
        // last-known-good content survives only under the hidden, GUID-suffixed backup that
        // nothing ever scanned for. This test seeds exactly that post-crash disk state (no code
        // path in this codebase can interrupt itself mid-move to produce it directly, so we place
        // the same artifact the crash would have left) and exercises the very next SaveAsync call
        // for the same path, which must self-heal instead of leaving the orphaned backup
        // forever undiscovered next to a `path` that has simply vanished.
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "saved.fxjson");
        var orphanedBackupPath = Path.Combine(temp.Path, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.bak");
        await File.WriteAllTextAsync(orphanedBackupPath, "orphaned original");
        var workbook = new Workbook("Saved");
        workbook.AddSheet("Sheet1");
        var adapter = new TestFileAdapter(save: (_, stream) =>
        {
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write("replacement");
        });

        await new WorkbookSaveService().SaveAsync(path, adapter, workbook);

        (await File.ReadAllTextAsync(path)).Should().Be("replacement");
        Directory.GetFiles(temp.Path, "*.bak").Should().BeEmpty(
            "the orphaned fallback backup must be consumed by the recovery scan on the next save, not left behind forever as undiscoverable clutter");
        Directory.GetFiles(temp.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task R115_SaveAsync_TargetExistsWithStaleFallbackBackup_DiscardsBackupWithoutTouchingTarget()
    {
        // Covers the sibling branch of the same recovery scan: when `path` already holds valid
        // content (either it was never touched by a fallback save, or an earlier fallback
        // placement succeeded but the process died before the trailing DeleteFile(backupPath)
        // cleanup ran), any backup found next to it is stale and must be discarded WITHOUT ever
        // touching `path` itself -- the scan must not confuse "a backup exists" with "path needs
        // restoring" when path is already fine.
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "saved.fxjson");
        await File.WriteAllTextAsync(path, "current content");
        var staleBackupPath = Path.Combine(temp.Path, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.bak");
        await File.WriteAllTextAsync(staleBackupPath, "stale backup content");
        var workbook = new Workbook("Saved");
        workbook.AddSheet("Sheet1");
        var adapter = new TestFileAdapter(save: (_, stream) =>
        {
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write("updated content");
        });

        await new WorkbookSaveService().SaveAsync(path, adapter, workbook);

        (await File.ReadAllTextAsync(path)).Should().Be("updated content");
        Directory.GetFiles(temp.Path, "*.bak").Should().BeEmpty(
            "a stale backup sitting next to an already-valid target must be cleaned up, not left as permanent clutter");
        Directory.GetFiles(temp.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task R115_SaveAsync_NoOrphanedBackupPresent_NormalSaveBehaviorUnchanged()
    {
        // No-regression sibling: the ordinary case (no fallback ever ran for this path, so no
        // backup sits next to it) must save exactly as before -- the new recovery scan must be a
        // silent no-op here, not alter the write, the move count, or leave any new artifact.
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "saved.fxjson");
        await File.WriteAllTextAsync(path, "original");
        var workbook = new Workbook("Saved");
        workbook.AddSheet("Sheet1");
        var adapter = new TestFileAdapter(save: (_, stream) =>
        {
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write("replacement");
        });

        await new WorkbookSaveService().SaveAsync(path, adapter, workbook);

        (await File.ReadAllTextAsync(path)).Should().Be("replacement");
        Directory.GetFiles(temp.Path, "*.bak").Should().BeEmpty();
        Directory.GetFiles(temp.Path, "*.tmp").Should().BeEmpty();
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
    public async Task SaveAsync_FileModifiedSinceOpen_ThrowsAndLeavesTargetAndTempUntouched()
    {
        // R83-services-doc-recovery-props-5-2: WorkbookOpenService.OpenFileStream never held the file
        // open/locked for the editing session and nothing compared the target's write time against
        // what was read at open, so a second writer's save between open and this save was clobbered
        // with zero warning. SaveAsync must now refuse to overwrite when the caller passes the write
        // time captured at open (WorkbookOpenResult.SourceLastWriteTimeUtc) and the file on disk no
        // longer matches it.
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "saved.fxjson");
        await File.WriteAllTextAsync(tempPath, "original");
        var openedAtUtc = File.GetLastWriteTimeUtc(tempPath);

        // Simulate a second writer touching the file after we "opened" it.
        await Task.Delay(20);
        await File.WriteAllTextAsync(tempPath, "someone else's edit");
        File.SetLastWriteTimeUtc(tempPath, DateTime.UtcNow);

        var workbook = new Workbook("Saved");
        workbook.AddSheet("Sheet1");
        var adapterInvoked = false;
        var adapter = new TestFileAdapter(save: (_, stream) =>
        {
            adapterInvoked = true;
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write("clobbered");
        });

        var act = async () => await new WorkbookSaveService().SaveAsync(
            tempPath,
            adapter,
            workbook,
            expectedLastWriteTimeUtc: openedAtUtc);

        await act.Should().ThrowAsync<WorkbookExternallyModifiedException>();
        adapterInvoked.Should().BeFalse("the save must bail out before writing over the other writer's change");
        (await File.ReadAllTextAsync(tempPath)).Should().Be("someone else's edit");
        Directory.GetFiles(temp.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_FileUnmodifiedSinceOpen_SavesNormallyWithExpectedWriteTimePassed()
    {
        // No-regression sibling for the external-modification check above: when the file on disk
        // still has the write time captured at open, the save must proceed exactly as before.
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "saved.fxjson");
        await File.WriteAllTextAsync(tempPath, "original");
        var openedAtUtc = File.GetLastWriteTimeUtc(tempPath);
        var workbook = new Workbook("Saved");
        workbook.AddSheet("Sheet1");
        var adapter = new TestFileAdapter(save: (_, stream) =>
        {
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write("replacement");
        });

        await new WorkbookSaveService().SaveAsync(
            tempPath,
            adapter,
            workbook,
            expectedLastWriteTimeUtc: openedAtUtc);

        (await File.ReadAllTextAsync(tempPath)).Should().Be("replacement");
        Directory.GetFiles(temp.Path, "*.tmp").Should().BeEmpty();
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

    [Fact]
    public async Task R119_SaveAsync_CanceledDuringWriting_StillWaitsForTheWriteToFinishBeforeReturning()
    {
        // No-regression sibling for R119_LoadAsync_CanceledDuringParsing_ReturnsPromptlyInsteadOf...
        // (WorkbookOpenServiceTests): SaveAsync's Writing stage serializes the LIVE, possibly
        // cross-window-shared Workbook, so -- unlike Open's Parsing stage -- it must NOT return to
        // the caller (and let the host re-enable user input) while adapter.Save is still reading
        // that object on another thread. This locks in that intentional asymmetry against a future
        // change accidentally making Save eager too and reintroducing the torn-snapshot race that
        // MainWindow.Backstage.cs's AdjustSaveGate exists to prevent.
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "canceled-mid-write.fxjson");
        var workbook = new Workbook("Canceled");
        workbook.AddSheet("Sheet1");
        using var writeStarted = new ManualResetEventSlim();
        var writeCompleted = false;
        var adapter = new TestFileAdapter(save: (_, stream) =>
        {
            writeStarted.Set();
            Thread.Sleep(300);
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write("payload");
            writeCompleted = true;
        });
        using var cancellation = new CancellationTokenSource();

        var saveTask = new WorkbookSaveService().SaveAsync(
            tempPath,
            adapter,
            workbook,
            cancellationToken: cancellation.Token);

        writeStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue("the adapter's Save must have started");
        cancellation.Cancel();

        var act = async () => await saveTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
        writeCompleted.Should().BeTrue(
            "SaveAsync must not return to the caller until the in-flight write has actually finished");
    }

    // ── R123: XLSM/XLTM/XLTX save-warning parity with XLSX ─────────────────────────────────────
    //
    // R123-appservices-save-warnings-xlsm-xltm-xltx: WorkbookSaveService.SaveAsync used to check
    // `adapter is XlsxFileAdapter` before threading a warnings list into the save. XlsmFileAdapter,
    // XltmFileAdapter and XltxFileAdapter are all separate sealed IFileAdapter wrappers around an
    // internal XlsxFileAdapter -- so a comment/hyperlink/merged-region/named-range/data-validation
    // item that fails to serialize during the SAME shared save pipeline (XlsxFileAdapter.SaveCore)
    // was silently dropped for these three formats with zero warning surfaced, while the identical
    // failure on a .xlsx save popped the "file saved with warnings" dialog. These tests go through
    // the REAL entry point (WorkbookSaveService.SaveAsync, exactly as MainWindow.Backstage.cs /
    // Avalonia MainWindow.cs call it), not the adapter directly, so they prove the fix reaches the
    // actual save call site the user's Save/Save-As action executes.

    private static Workbook CreateWorkbookWithInvalidComment()
    {
        var workbook = new Workbook("Macro");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));
        // A comment address beyond the maximum worksheet row is rejected by ClosedXML's
        // CreateComment (mirrors XlsxSaveWarningsTests.SaveWithWarnings_InvalidComment_ReturnsWarning),
        // so the save pipeline catches the exception, skips the comment, and records a warning
        // string instead of losing the item with no trace.
        var invalidAddress = new CellAddress(sheet.Id, CellAddress.MaxRow + 1, 1);
        sheet.Comments[invalidAddress] = "Cannot be written.";
        return workbook;
    }

    [Fact]
    public async Task R123_SaveAsync_XlsmAdapterWithInvalidComment_SurfacesSaveWarnings()
    {
        // FAIL-BEFORE: with the old `adapter is XlsxFileAdapter` check, XlsmFileAdapter never
        // matched (it is a separate sealed class composing an XlsxFileAdapter internally), so
        // this returned an empty list regardless of the dropped comment.
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "macro.xlsm");
        var workbook = CreateWorkbookWithInvalidComment();

        var warnings = await new WorkbookSaveService().SaveAsync(path, new XlsmFileAdapter(), workbook);

        warnings.Should().NotBeEmpty(
            "an .xlsm save that silently drops a comment must surface the same warning a .xlsx save would");
        warnings.Should().Contain(w => w.Contains("[comment]", StringComparison.OrdinalIgnoreCase));
        File.Exists(path).Should().BeTrue("the file must still be written even though an item was skipped");
    }

    [Fact]
    public async Task R123_SaveAsync_XltmAdapterWithInvalidComment_SurfacesSaveWarnings()
    {
        // Sibling family member: XltmFileAdapter also routes through
        // XlsxFileAdapter.SavePreservingVbaProject and must get the same treatment as XlsmFileAdapter.
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "macro.xltm");
        var workbook = CreateWorkbookWithInvalidComment();

        var warnings = await new WorkbookSaveService().SaveAsync(path, new XltmFileAdapter(), workbook);

        warnings.Should().NotBeEmpty("an .xltm save must surface dropped-item warnings exactly like .xlsx");
        warnings.Should().Contain(w => w.Contains("[comment]", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task R123_SaveAsync_XltxAdapterWithInvalidComment_SurfacesSaveWarnings()
    {
        // Sibling family member: XltxFileAdapter routes through the plain (non-VBA-preserving)
        // XlsxFileAdapter.Save pipeline and must also get the same treatment.
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "template.xltx");
        var workbook = CreateWorkbookWithInvalidComment();

        var warnings = await new WorkbookSaveService().SaveAsync(path, new XltxFileAdapter(), workbook);

        warnings.Should().NotBeEmpty("an .xltx save must surface dropped-item warnings exactly like .xlsx");
        warnings.Should().Contain(w => w.Contains("[comment]", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task R123_SaveAsync_XlsmAdapterCleanWorkbook_NoRegressionReturnsEmptyWarnings()
    {
        // No-regression sibling: a workbook with nothing to drop must still save through the real
        // XlsmFileAdapter with an empty warnings list (not, e.g., every save always reporting a
        // spurious warning once the adapter starts collecting them).
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "clean.xlsm");
        var workbook = new Workbook("Macro");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));

        var warnings = await new WorkbookSaveService().SaveAsync(path, new XlsmFileAdapter(), workbook);

        warnings.Should().BeEmpty("a workbook with nothing to drop must not produce spurious save warnings");
        File.Exists(path).Should().BeTrue();
    }

    private sealed class TestWorkbookSaveFileOperations : IWorkbookSaveFileOperations
    {
        public Exception? ReplaceException { get; init; }

        /// <summary>
        /// Called for every MoveFile invocation with (sourcePath, destinationPath, overwrite);
        /// return a non-null exception to throw instead of performing the move. Lets a test target
        /// a specific move in the fallback sequence (e.g. only the temp-into-target placement move,
        /// not the vacate-to-backup move or the restore-from-backup move) regardless of which
        /// overwrite flag the implementation happens to pass for that step.
        /// </summary>
        public Func<string, string, bool, Exception?>? MoveFileException { get; init; }

        /// <summary>
        /// When set, simulates a partial/interrupted byte-level move: instead of performing an
        /// atomic rename, writes truncated garbage bytes to <paramref name="destinationPath"/> (as
        /// a real File.Move copy-then-delete fallback can do mid-copy on a disk-full/permission/
        /// network-drop failure) and then throws, the FIRST time a move targets
        /// <see cref="PartialWriteFailureDestinationPath"/>. Only the first match fires (not
        /// subsequent ones) because the real target path can legitimately be the destination of
        /// two different moves in the fallback sequence -- the risky placement move (which this
        /// simulates failing) and, only if that fails, the later restore-from-backup move (which
        /// must be allowed to actually succeed so the fixed implementation can recover). Source is
        /// not deleted, matching a real interrupted copy-then-delete (the delete only happens
        /// after a successful copy).
        /// </summary>
        public string? PartialWriteFailureDestinationPath { get; init; }

        private bool _partialWriteFailureFired;

        public int ReplaceCallCount { get; private set; }

        public int MoveCallCount { get; private set; }

        public int OverwriteMoveCallCount { get; private set; }

        public bool FileExists(string path) => File.Exists(path);

        public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);

        public void ReplaceFile(string sourcePath, string destinationPath)
        {
            ReplaceCallCount++;
            if (ReplaceException is not null)
                throw ReplaceException;

            File.Replace(sourcePath, destinationPath, null, ignoreMetadataErrors: true);
        }

        public void MoveFile(string sourcePath, string destinationPath, bool overwrite)
        {
            MoveCallCount++;
            if (overwrite)
                OverwriteMoveCallCount++;

            if (!_partialWriteFailureFired &&
                PartialWriteFailureDestinationPath is not null &&
                string.Equals(Path.GetFullPath(destinationPath), Path.GetFullPath(PartialWriteFailureDestinationPath), StringComparison.OrdinalIgnoreCase))
            {
                _partialWriteFailureFired = true;
                File.WriteAllBytes(destinationPath, "CORRUPT-PARTIAL-BYTES"u8.ToArray());
                throw new IOException("simulated partial move failure mid-copy");
            }

            var exception = MoveFileException?.Invoke(sourcePath, destinationPath, overwrite);
            if (exception is not null)
                throw exception;

            if (overwrite)
                File.Move(sourcePath, destinationPath, overwrite: true);
            else
                File.Move(sourcePath, destinationPath);
        }

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite) =>
            File.Copy(sourcePath, destinationPath, overwrite);

        public void DeleteFile(string path) => File.Delete(path);

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern) =>
            Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, searchPattern)
                : [];
    }
}
