using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R92-render-localization-chrome-5-3 (MED) and R92-render-localization-chrome-5-4 (LOW): two
/// hardcoded, English-only status messages in the Avalonia shell that should have gone through the
/// shared resx catalog like their neighbors.
///
/// 5-3: FormatRemoveDuplicatesStatus built its whole sentence in English with a naive binary
/// singular/plural switch ("row"/"rows") and no resx key at all, unlike the WPF host's own
/// MainWindowMessage_RemoveDuplicatesRemovedRows (MainWindow.DataCommands.cs) -- reused here rather
/// than adding a duplicate key. The "Remove Duplicates" dialog/result titles were likewise raw
/// literals instead of the shared MainWindowMessage_RemoveDuplicatesTitle key.
///
/// 5-4: SelectGoToSpecial's multi-range branch spliced a raw "{0} cells" English fragment into the
/// otherwise-localized MainLoc_SelectedX template. Since neither shell had a bare "{0} cells" key,
/// this fix adds MainLoc_CellsCount to the shared catalog and routes the fragment through it.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R92_LocalizedRemoveDuplicatesAndGoToSpecialStatusTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // ── 5-3: Remove Duplicates status message ───────────────────────────────────────────────────

    [Fact]
    public async Task FormatRemoveDuplicatesStatus_UsesSharedCatalogMessage_NotHardcodedEnglishSentence()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("Dupes");
            window.Session.SelectSheet(sheet.Id);
            SeedOneDuplicateRow(sheet);
            var plan = BuildRemoveDuplicatesPlan(sheet, lastRow: 3);

            var result = window.Session.ExecuteRemoveDuplicatesPlan(plan);
            result.Success.Should().BeTrue();
            result.RemovedRowCount.Should().Be(1);

            // This is the exact regression: before the fix this was the hardcoded, un-catalogued
            // sentence built by string interpolation
            // ($"Removed {n} duplicate {row/rows} from {range}"). After the fix it must be exactly
            // the shared catalog's own resx-backed sentence -- the same one the WPF host shows.
            InvokeFormatRemoveDuplicatesStatus(result).Should().Be("Removed 1 duplicate rows.");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FormatRemoveDuplicatesStatus_TwoDuplicateRows_PluralCountStillFormatsCorrectly_NoRegression()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("Dupes");
            window.Session.SelectSheet(sheet.Id);
            SeedOneDuplicateRow(sheet);
            // Add a second duplicate of the same value so two rows get removed.
            sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("dup"));
            var plan = BuildRemoveDuplicatesPlan(sheet, lastRow: 4);

            var result = window.Session.ExecuteRemoveDuplicatesPlan(plan);
            result.Success.Should().BeTrue();
            result.RemovedRowCount.Should().Be(2);

            InvokeFormatRemoveDuplicatesStatus(result).Should().Be("Removed 2 duplicate rows.");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── 5-4: Go To Special multi-range status ───────────────────────────────────────────────────

    [Fact]
    public async Task SelectGoToSpecial_CommentsOnDisjointCells_StatusUsesLocalizedCellsCountNotRawEnglish()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            // Two comments far enough apart that SelectionRangeService compresses them into two
            // disjoint ranges, driving the (previously un-localized) multi-range branch.
            sheet.Comments[new CellAddress(sheet.Id, 1, 1)] = "Note A";
            sheet.Comments[new CellAddress(sheet.Id, 10, 5)] = "Note B";
            window.Session.SelectRange(new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 20, 10)));

            var succeeded = InvokeSelectGoToSpecial(window, GoToSpecialKind.Comments);

            succeeded.Should().BeTrue();
            // Before the fix this was "Selected 2 cells" with "cells" a raw, never-catalogued
            // English literal spliced into the localized "Selected {0}" template.
            window.StatusTextForTest.Text.Should().Be("Selected 2 cells");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SelectGoToSpecial_SingleRange_StillUsesPlainRangeReference_NoRegression()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            sheet.Comments[new CellAddress(sheet.Id, 3, 3)] = "Only note";
            window.Session.SelectRange(new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 20, 10)));

            var succeeded = InvokeSelectGoToSpecial(window, GoToSpecialKind.Comments);

            succeeded.Should().BeTrue();
            // The single-range branch (FormatRangeReference) was never part of this finding and
            // must still produce a bare, culture-neutral range reference, not a "cells" count.
            window.StatusTextForTest.Text.Should().Be("Selected C3");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── Source-contract checks ──────────────────────────────────────────────────────────────────
    // The neutral-locale English text for "{0} cells" is byte-identical whether it comes from the
    // shared catalog or a raw interpolated literal, so the behavioral test above cannot by itself
    // distinguish "routed through the catalog" from "still hardcoded" -- these checks anchor on the
    // actual source text so a revert of the fix (back to the raw interpolation) fails them.

    [Fact]
    public void SelectGoToSpecial_MultiRangeBranch_UsesLocalizedCellsCountKey_NotRawInterpolation()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var methodStart = source.IndexOf("private bool SelectGoToSpecial(", StringComparison.Ordinal);
        methodStart.Should().BeGreaterThanOrEqualTo(0, "SelectGoToSpecial must still exist");
        var methodEnd = source.IndexOf("\n    private", methodStart + 1, StringComparison.Ordinal);
        methodEnd.Should().BeGreaterThan(methodStart);
        var method = source[methodStart..methodEnd];

        method.Should().Contain(
            "UiText.Format(\"MainLoc_CellsCount\", result.MatchCount)",
            "the multi-range status fragment must route through the shared catalog");
        method.Should().NotContain(
            "$\"{result.MatchCount} cells\"",
            "the raw, never-catalogued English interpolation must be gone");
    }

    [Fact]
    public void FormatRemoveDuplicatesStatus_And_RemoveDuplicatesDialogTitles_UseSharedCatalogKeys()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot("src", "FreeX.App.Avalonia", "MainWindow.cs");

        source.Should().Contain(
            "UiText.Format(\"MainWindowMessage_RemoveDuplicatesRemovedRows\", result.RemovedRowCount)",
            "the removed-rows status must reuse the WPF host's own shared catalog message");
        source.Should().NotContain(
            "duplicate {rowLabel}",
            "the hardcoded English-only singular/plural switch must be gone");
        source.Should().Contain(
            "UiText.Get(\"MainWindowMessage_RemoveDuplicatesTitle\")",
            "the Remove Duplicates dialog/result titles must reuse the shared catalog title key");
    }

    // ── Test helpers ─────────────────────────────────────────────────────────────────────────────

    private static void SeedOneDuplicateRow(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("dup"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("dup"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("unique"));
    }

    private static RemoveDuplicatesPlan BuildRemoveDuplicatesPlan(Sheet sheet, uint lastRow)
    {
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, lastRow, 1));
        var columns = new List<RemoveDuplicateColumnChoice> { new(0, "Column A", true) };
        return new RemoveDuplicatesPlan(range, range, HasHeaders: false, columns);
    }

    private static string InvokeFormatRemoveDuplicatesStatus(WorkbookRemoveDuplicatesResult result)
    {
        var method = typeof(MainWindow).GetMethod(
            "FormatRemoveDuplicatesStatus", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull("FormatRemoveDuplicatesStatus must still exist as the Remove Duplicates status formatter");
        return (string)method!.Invoke(null, [result])!;
    }

    private static bool InvokeSelectGoToSpecial(MainWindow window, GoToSpecialKind kind)
    {
        var method = typeof(MainWindow).GetMethod("SelectGoToSpecial", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull("SelectGoToSpecial must still exist as the Go To Special selection handler");
        return (bool)method!.Invoke(window, [kind, null])!;
    }
}
