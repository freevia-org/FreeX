using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for the shared-cancellation F2 finding: OpenFileAsync's local
/// ApplyOpenedWorkbookAsync completion callback used to swap in the newly opened workbook
/// (ReplaceWorkbookSession), rebuild the recalc dependency graph, refresh the Watch Window,
/// rename/retitle the workbook, and mark it saved -- all BEFORE checking
/// cancellationToken.ThrowIfCancellationRequested(). A Cancel click landing in that window left
/// an unprotected workbook live as _workbook (skipping RefreshSheetTabs, the unsupported/load
/// warnings, and -- critically -- ApplyWorkbookReadOnlyOpenPolicy, the sole gate for a
/// write-reservation password or "Read-Only Recommended" workbook) while the outer catch
/// silently reported the open as merely "canceled".
///
/// The fix moves the cancellation checkpoint to the very first statement of
/// ApplyOpenedWorkbookAsync -- before ReplaceWorkbookSession or any other mutation -- matching
/// the Avalonia host's equivalent callback (src/FreeX.App.Avalonia/MainWindow.cs), which throws
/// before its own ReplaceSession call. These are source-level tests (like the rest of
/// MainWindowSourceHygieneTests.Backstage.cs) because OpenFileAsync is a private WPF host method
/// wired through live dialogs/dispatcher pumping that isn't practically unit-invokable.
/// </summary>
public sealed class R156_OpenFileCancellationCheckpointOrderingTests
{
    private static string ApplyOpenedWorkbookAsyncSource()
    {
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
        var openMethod = SourceMethodExtractor.ExtractMethodSource(backstageSource, "private async Task OpenFileAsync(");
        return SourceMethodExtractor.ExtractMethodSource(openMethod, "Task ApplyOpenedWorkbookAsync(");
    }

    [Fact]
    public void ApplyOpenedWorkbookAsync_ChecksCancellationBeforeReplacingTheWorkbookSession()
    {
        // This is the finding's core defect: before the fix, ReplaceWorkbookSession(...) (the
        // workbook swap) appeared BEFORE cancellationToken.ThrowIfCancellationRequested() in this
        // method's source, so the swap always ran even when the operation was about to be
        // reported as canceled.
        var callback = ApplyOpenedWorkbookAsyncSource();

        var throwIndex = callback.IndexOf("cancellationToken.ThrowIfCancellationRequested();", StringComparison.Ordinal);
        var swapIndex = callback.IndexOf("ReplaceWorkbookSession(new StartupWorkbookLoadResult(", StringComparison.Ordinal);

        throwIndex.Should().BeGreaterThanOrEqualTo(0, "the callback should still check cancellation");
        swapIndex.Should().BeGreaterThanOrEqualTo(0, "the callback should still swap in the opened workbook");
        throwIndex.Should().BeLessThan(swapIndex,
            "the cancellation checkpoint must run before the workbook is swapped in, or a cancel " +
            "lands with the new workbook already live but reported as canceled");
    }

    [Fact]
    public void ApplyOpenedWorkbookAsync_ChecksCancellationBeforeEveryOtherOpenSideEffect()
    {
        // Belt-and-suspenders over the previous test: every other mutation the finding named
        // (recalc dependency rebuild, Watch Window refresh, title/dirty-state bookkeeping, the
        // sheet-tab refresh, and the read-only-open policy gate) must also come after the single
        // checkpoint -- not just the workbook swap itself.
        var callback = ApplyOpenedWorkbookAsyncSource();
        var throwIndex = callback.IndexOf("cancellationToken.ThrowIfCancellationRequested();", StringComparison.Ordinal);
        throwIndex.Should().BeGreaterThanOrEqualTo(0);

        string[] mustFollowCheckpoint =
        {
            "_recalcEngine.RebuildFormulaDependencies(_workbook);",
            "_watchWindowDialog?.Refresh();",
            "_workbook.Name = plan.DisplayName;",
            "MarkWorkbookSaved();",
            "RefreshSheetTabs();",
            "ApplyWorkbookReadOnlyOpenPolicy(_workbook, target.Path);",
        };

        foreach (var statement in mustFollowCheckpoint)
        {
            var statementIndex = callback.IndexOf(statement, StringComparison.Ordinal);
            statementIndex.Should().BeGreaterThanOrEqualTo(0, $"callback should still contain: {statement}");
            statementIndex.Should().BeGreaterThan(throwIndex,
                $"'{statement}' must run after the cancellation checkpoint, not before it");
        }
    }

    [Fact]
    public void ApplyOpenedWorkbookAsync_HasExactlyOneCancellationCheckpoint()
    {
        // Sibling/no-regression guard: once the workbook swap has been committed past the single
        // top-of-method checkpoint, nothing later in the callback may re-check cancellation.
        // A second check between the swap and the read-only-policy/sheet-tab-refresh calls would
        // silently reintroduce the same "workbook already swapped, but its safety gates were
        // skipped" hazard the fix closes -- just at a later point in the same method. This mirrors
        // the Avalonia host's equivalent callback, which also throws exactly once, at the top,
        // before its ReplaceSession call, and never checks again afterward.
        var callback = ApplyOpenedWorkbookAsyncSource();

        var occurrences = 0;
        var searchFrom = 0;
        const string needle = "ThrowIfCancellationRequested();";
        while (true)
        {
            var index = callback.IndexOf(needle, searchFrom, StringComparison.Ordinal);
            if (index < 0) break;
            occurrences++;
            searchFrom = index + needle.Length;
        }

        occurrences.Should().Be(1,
            "ApplyOpenedWorkbookAsync should check cancellation exactly once, before any state mutation");
    }

    [Fact]
    public void ApplyOpenedWorkbookAsync_StillAppliesTheReadOnlyOpenPolicyAndRefreshesSheetTabs()
    {
        // No-regression check for the adjacent, already-correct behavior: the fix must not
        // accidentally delete these calls while relocating the cancellation checkpoint -- both
        // still have to run on every successful (non-canceled) open.
        var callback = ApplyOpenedWorkbookAsyncSource();

        callback.Should().Contain("RefreshSheetTabs();");
        callback.Should().Contain("ApplyWorkbookReadOnlyOpenPolicy(_workbook, target.Path);");
        callback.Should().Contain("ShowUnsupportedXlsxFeatureOpenWarningIfNeeded();");
        callback.Should().Contain("ShowXlsxLoadWarningsIfNeeded(result.LoadWarnings);");
    }
}
