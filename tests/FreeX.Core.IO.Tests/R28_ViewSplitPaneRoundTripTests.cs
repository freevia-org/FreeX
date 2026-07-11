using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for R28-view-zoom-sheetpr-commands-2: a genuine (non-frozen) Excel window
/// split (View &gt; Split, <c>state="split"</c>) created and saved by FreeX itself must survive the
/// NEXT load of that same file. XlsxWorksheetViewWriter correctly persists xSplit/ySplit as
/// twentieths-of-a-point pixel positions (see FreeXR13S4Tests.R13_view_state_2_...), but
/// ClosedXML's own SheetView.SplitRow/SplitColumn are only populated for its freeze-pane API and
/// are always 0/null for a <c>state="split"</c> pane -- so reload used to silently drop the split
/// entirely (both SplitRow and SplitColumn came back null, and it did not even become a freeze).
///
/// These tests exercise the READ side by driving a full save-then-reload round trip through the
/// public adapter, so they also pin the sibling cases that must keep working:
///  - a frozen pane (state="frozen"/"frozenSplit") round trip, which was never broken and must
///    remain unaffected by the split-recovery fallback added for this fix.
///  - a single-axis split (only one of SplitRow/SplitColumn set), which must still recover just
///    that axis.
///
/// See also XlsxWorksheetViewMSplitTopLeftTests, which pins that a state="split" pane loaded from
/// a file whose row/column sizing no longer matches the persisted pixel position (so the twips
/// value cannot be cleanly inverted back to a row/column index) must remain unrecoverable -- these
/// new tests must not regress that safety behavior.
/// </summary>
public sealed class R28_ViewSplitPaneRoundTripTests
{
    private static void PrepareLoadedWorkbookForEdit(Workbook workbook)
    {
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
    }

    private static byte[] CreateSourcePackage()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell("A1").Value = "original value";
            sheet.Cell("B2").Value = 123.45;
            workbook.SaveAs(stream);
        }

        return stream.ToArray();
    }

    private static Workbook LoadAndPrepareForEdit(byte[] sourceBytes, XlsxFileAdapter adapter)
    {
        using var source = new MemoryStream(sourceBytes, writable: false);
        var workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);
        return workbook;
    }

    [Fact]
    public void SplitPane_TwoAxisSplit_SurvivesSaveAndReload()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = LoadAndPrepareForEdit(CreateSourcePackage(), adapter);
        var sheet = workbook.GetSheetAt(0);

        // Pin the row/column metrics that feed the twips conversion so the round trip is
        // deterministic, matching FreeX.Core.Model.Sheet's own documented defaults.
        sheet.DefaultRowHeight = 20.0;
        sheet.DefaultColumnWidth = 8.43;
        sheet.RowHeights.Clear();
        sheet.ColumnWidths.Clear();

        // A user-created View > Split at row index 5 / column index 4 (SetSplitPanesCommand).
        sheet.SplitRow = 5u;
        sheet.SplitColumn = 4u;
        sheet.FrozenRows = 0;
        sheet.FrozenCols = 0;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        // The next, independent load of that same file (a fresh adapter instance, matching how a
        // user reopening the file behaves) must recover the split rather than silently losing it.
        var reloadedAdapter = new XlsxFileAdapter();
        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = reloadedAdapter.Load(reloadStream);
        var reloadedSheet = reloaded.GetSheetAt(0);

        reloadedSheet.SplitRow.Should().Be(5u, "a FreeX-authored split must survive its own save+reload round trip");
        reloadedSheet.SplitColumn.Should().Be(4u);
        reloadedSheet.FrozenRows.Should().Be(0, "a real split must never turn into a freeze");
        reloadedSheet.FrozenCols.Should().Be(0);
    }

    [Fact]
    public void SplitPane_RowOnlySplit_SurvivesSaveAndReload()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = LoadAndPrepareForEdit(CreateSourcePackage(), adapter);
        var sheet = workbook.GetSheetAt(0);

        sheet.DefaultRowHeight = 20.0;
        sheet.DefaultColumnWidth = 8.43;
        sheet.RowHeights.Clear();
        sheet.ColumnWidths.Clear();

        // A single-axis split (only the horizontal divider), a legitimate SetSplitPanesCommand
        // input where splitColumn is null.
        sheet.SplitRow = 3u;
        sheet.SplitColumn = null;
        sheet.FrozenRows = 0;
        sheet.FrozenCols = 0;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        var reloadedAdapter = new XlsxFileAdapter();
        using var reloadStream = new MemoryStream(saved.ToArray(), writable: false);
        var reloaded = reloadedAdapter.Load(reloadStream);
        var reloadedSheet = reloaded.GetSheetAt(0);

        reloadedSheet.SplitRow.Should().Be(3u, "a single-axis split must recover the axis that was actually set");
        reloadedSheet.SplitColumn.Should().BeNull("no column split was ever created");
    }

    [Fact]
    public void FrozenPanes_SiblingCase_StillSurvivesSaveAndReload()
    {
        // Sibling already-working case: a genuine freeze (state="frozen"/"frozenSplit", where
        // xSplit/ySplit are literal row/column counts per OOXML) must remain unaffected by the
        // split-recovery fallback added for this fix.
        var adapter = new XlsxFileAdapter();
        var workbook = LoadAndPrepareForEdit(CreateSourcePackage(), adapter);
        var sheet = workbook.GetSheetAt(0);

        sheet.FrozenRows = 2u;
        sheet.FrozenCols = 1u;
        sheet.SplitRow = null;
        sheet.SplitColumn = null;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        var reloadedAdapter = new XlsxFileAdapter();
        using var reloadStream = new MemoryStream(saved.ToArray(), writable: false);
        var reloaded = reloadedAdapter.Load(reloadStream);
        var reloadedSheet = reloaded.GetSheetAt(0);

        reloadedSheet.FrozenRows.Should().Be(2u);
        reloadedSheet.FrozenCols.Should().Be(1u);
        reloadedSheet.SplitRow.Should().BeNull();
        reloadedSheet.SplitColumn.Should().BeNull();
    }
}
