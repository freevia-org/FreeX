using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R64-io-hyperlink-6-1: a range-anchored internal hyperlink (location e.g. "Sheet1!B2:D5") was
/// parsed to a full range by TryNavigateToWorkbookReference, but then handed only
/// <c>range.Start</c> to NavigateToCell -- collapsing the target down to the single top-left
/// cell instead of selecting the whole range, unlike Excel and unlike the WPF host's own Name-Box
/// navigation (NavigateNameBoxTo) and the shared WorkbookSession.GoToReference-&gt;GoToRange path.
/// </summary>
public sealed class R64_HyperlinkRangeNavigationTests
{
    [Fact]
    public void TryNavigateToWorkbookReference_RangeTarget_SelectsWholeRange()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var reference = $"{sheet.Name}!B2:D5";

                var navigated = (bool)R49MainWindowTestHarness.Invoke(
                    window, "TryNavigateToWorkbookReference", reference)!;

                navigated.Should().BeTrue();

                var expected = new GridRange(
                    new CellAddress(sheet.Id, 2, 2),
                    new CellAddress(sheet.Id, 5, 4));
                window.SheetGrid.SelectedRange.Should().Be(
                    expected,
                    "a range-anchored hyperlink target must select the whole range, not collapse to its top-left cell");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void TryNavigateToWorkbookReference_SingleCellTarget_SelectsSingleCell()
    {
        // Sibling no-regression case: a plain single-cell hyperlink target must still collapse to
        // just that cell, exactly as before this fix.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var reference = $"{sheet.Name}!C7";

                var navigated = (bool)R49MainWindowTestHarness.Invoke(
                    window, "TryNavigateToWorkbookReference", reference)!;

                navigated.Should().BeTrue();

                var expected = new GridRange(
                    new CellAddress(sheet.Id, 7, 3),
                    new CellAddress(sheet.Id, 7, 3));
                window.SheetGrid.SelectedRange.Should().Be(expected);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }
}
